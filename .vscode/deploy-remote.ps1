# OmniCard - build + publish to a REMOTE server (IIS, or the Windows service install-service.ps1 sets
# up), over a network share or Web Deploy (IIS only).
# Run via the "Publish to Remote Server" VS Code task (no elevation needed).
# Settings: .vscode/deploy-remote.json holds the tracked defaults; your values go in
# .vscode/deploy-remote.local.json (git-ignored, created from the defaults on the first run).
# One-time server setup you handle yourself (see OmniCard.Web/README.md > Publish to a remote server):
#   - IIS site + "No Managed Code" app pool, .NET 10 Hosting Bundle, SQL Server, DataDirectory grants
#     (the local .vscode/deploy.ps1 shows what those look like)
#   - Share method: the site's physical folder shared with Change permission for the deploy account
#   - WebDeploy method: Web Deploy's Remote Agent Service (or WMSVC on Windows Server) running
#
# -InstallService (the "Publish to Remote Server (service)" task): also runs install-service.ps1 on the
# server after the copy, elevated over PowerShell remoting, so the Windows service is (re)installed and
# started with no login on the server. Share method only. One-time setup: Enable-PSRemoting on the
# server, and the server in this PC's WinRM TrustedHosts (the script prints both if it can't connect).

param([switch]$InstallService)

$ErrorActionPreference = 'Stop'

$Repo       = Split-Path -Parent $PSScriptRoot   # repo root (.vscode's parent)
$Defaults   = Join-Path $PSScriptRoot 'deploy-remote.json'
$LocalFile  = Join-Path $PSScriptRoot 'deploy-remote.local.json'
$SecretFile = Join-Path $PSScriptRoot 'deploy-remote.local.secret'
$AdminSecretFile = Join-Path $PSScriptRoot 'deploy-remote.local.admin.secret'
$Temp       = [IO.Path]::GetTempPath()
$Staging    = Join-Path $Temp 'OmniCardRemotePublish'
$ServerCopy = Join-Path $Temp 'omnicard-remote-appsettings.json'

$script:MappedShare = $null
$script:Session = $null
function Close-Share {
    if ($script:MappedShare) { net use $script:MappedShare /delete /y 2>$null | Out-Null; $script:MappedShare = $null }
    if ($script:Session) { Remove-PSSession $script:Session -ErrorAction SilentlyContinue; $script:Session = $null }
}
function Fail($msg) { Write-Host $msg -ForegroundColor Red; Close-Share; exit 1 }

# ---- 0. Settings: your local values over the tracked defaults ----
if (-not (Test-Path -LiteralPath $LocalFile)) {
    Copy-Item -LiteralPath $Defaults -Destination $LocalFile
    Write-Host "Created $LocalFile" -ForegroundColor Yellow
    Write-Host "Fill in your server's values there (the file is git-ignored), then run the task again." -ForegroundColor Yellow
    exit 1
}
$cfg = Get-Content -LiteralPath $Defaults -Raw | ConvertFrom-Json
try { $local = Get-Content -LiteralPath $LocalFile -Raw | ConvertFrom-Json } catch { Fail "Can't parse ${LocalFile}: $_" }
foreach ($p in $local.PSObject.Properties) { $cfg | Add-Member -NotePropertyName $p.Name -NotePropertyValue $p.Value -Force }

function Expand($s) { "$s".Replace('{Server}', $cfg.Server).Replace('{SiteName}', $cfg.SiteName) }
$Method  = $cfg.Method
$SiteUrl = Expand $cfg.SiteUrl
if ($Method -notin 'Share', 'WebDeploy') { Fail "Method must be 'Share' or 'WebDeploy' (got '$Method') - see $LocalFile." }

# Passwords are never stored in the settings files: they're prompted for, or (RememberPassword) kept in
# deploy-remote.local[.admin].secret, DPAPI-encrypted so only your Windows account on this PC can read them back.
$script:Passwords = @{}
function Get-SecurePassword([string]$User, [string]$File) {
    if ($script:Passwords[$File]) { return $script:Passwords[$File] }
    $secure = $null
    if ($cfg.RememberPassword -and (Test-Path -LiteralPath $File)) {
        try { $secure = (Get-Content -LiteralPath $File -Raw).Trim() | ConvertTo-SecureString } catch { $secure = $null }
    }
    if (-not $secure) {
        $secure = Read-Host "Password for $User on $($cfg.Server)" -AsSecureString
        if ($cfg.RememberPassword) { $secure | ConvertFrom-SecureString | Set-Content -LiteralPath $File }
    }
    $script:Passwords[$File] = $secure
    $secure
}
function Get-DeployPassword {
    (New-Object System.Management.Automation.PSCredential 'deploy', (Get-SecurePassword $cfg.UserName $SecretFile)).GetNetworkCredential().Password
}
$WrongPasswordHint = "If the server password changed, delete $SecretFile and run the task again."

# msdeploy parses its own provider syntax and is picky about the quoting PowerShell adds to native
# arguments, so hand it one verbatim command line through Start-Process.
function Invoke-MsDeploy([string]$ArgLine, [string]$OutFile) {
    $p = @{ FilePath = $MsDeploy; ArgumentList = $ArgLine; NoNewWindow = $true; Wait = $true; PassThru = $true }
    if ($OutFile) { $p.RedirectStandardOutput = $OutFile; $p.RedirectStandardError = "$OutFile.err" }
    (Start-Process @p).ExitCode
}

# ---- 1. Reach the server first, so a bad setting fails before the slow build ----
$ServerHasSettings = $false
if ($Method -eq 'Share') {
    $Share = (Expand $cfg.SharePath).TrimEnd('\')
    $shareRoot = '\\' + ((($Share.TrimStart('\')) -split '\\')[0..1] -join '\')
    # "Can we list it?" - Test-Path alone reports a share as present even when listing it is denied.
    function Test-Share { try { $null = Get-ChildItem -LiteralPath $Share -Force -ErrorAction Stop; $true } catch { $false } }
    if (-not (Test-Share)) {
        if (-not $cfg.UserName) { Fail "Can't open $Share with your Windows login. Set UserName in $LocalFile to an account on the server with Change permission on the share." }
        Write-Host "Connecting to $shareRoot as $($cfg.UserName)" -ForegroundColor Cyan
        net use $shareRoot (Get-DeployPassword) "/user:$($cfg.UserName)" /persistent:no | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Fail "Couldn't connect to $shareRoot. Check UserName and the password. $WrongPasswordHint (System error 1219 means this PC already has a connection to the server under another account: run 'net use' to find it and 'net use <path> /delete' to drop it.)"
        }
        $script:MappedShare = $shareRoot
        if (-not (Test-Share)) { Fail "Connected to $shareRoot, but can't open $Share as $($cfg.UserName) (missing, or no permission)." }
    }
    # The deploy MIRRORS the build into this folder (deleting everything else), so refuse anything that
    # doesn't look like an OmniCard site folder: it must hold a web.config already, or be empty.
    $existing = @(Get-ChildItem -LiteralPath $Share -Force)
    if ($existing.Count -gt 0 -and -not (Test-Path -LiteralPath (Join-Path $Share 'web.config'))) {
        Fail "$Share isn't empty and has no web.config, so it doesn't look like the IIS site folder. Refusing to mirror into it - check SharePath."
    }
    $ServerHasSettings = Test-Path -LiteralPath (Join-Path $Share 'appsettings.json')
} else {
    $MsDeploy = $cfg.MsDeployPath
    if (-not (Test-Path -LiteralPath $MsDeploy)) { Fail "msdeploy.exe not found at $MsDeploy. Install Web Deploy on this PC or fix MsDeployPath." }
    $WebDeployUrl = Expand $cfg.WebDeployUrl
    $Remote = "computerName='$WebDeployUrl',authType='$($cfg.WebDeployAuthType)',includeAcls='False'"
    if ($cfg.UserName) { $Remote += ",userName='$($cfg.UserName)',password='$(Get-DeployPassword)'" }
    Write-Host "Checking Web Deploy at $WebDeployUrl" -ForegroundColor Cyan
    $dump = Join-Path $Temp 'omnicard-msdeploy-dump.txt'
    $code = Invoke-MsDeploy "-verb:dump -source:contentPath='$($cfg.SiteName)',$Remote -allowUntrusted" $dump
    if ($code -ne 0) {
        Get-Content -LiteralPath "$dump.err" -ErrorAction SilentlyContinue | Write-Host
        Fail "Can't reach site '$($cfg.SiteName)' through Web Deploy at $WebDeployUrl. Check the URL, AuthType, UserName and password. $WrongPasswordHint"
    }
    $ServerHasSettings = [bool](Select-String -LiteralPath $dump -Pattern '\\appsettings\.json\s*$' -Quiet)
    Remove-Item -LiteralPath $dump, "$dump.err" -ErrorAction SilentlyContinue
}

# ---- 1b. (-InstallService) PowerShell remoting session as a server admin ----
# Runs install-service.ps1 after the copy. ServiceAdminUserName (blank = UserName) must be an
# administrator on the server; its password is kept apart from the share account's.
$ServiceName = if ($cfg.ServiceName) { $cfg.ServiceName } else { 'OmniCard' }
if ($InstallService) {
    if ($Method -ne 'Share') { Fail "-InstallService needs Method 'Share' (Web Deploy is IIS only) - see $LocalFile." }
    $adminUser = if ($cfg.ServiceAdminUserName) { $cfg.ServiceAdminUserName } else { $cfg.UserName }
    $adminFile = if ($cfg.ServiceAdminUserName -and $cfg.ServiceAdminUserName -ne $cfg.UserName) { $AdminSecretFile } else { $SecretFile }
    $sessionArgs = @{ ComputerName = $cfg.Server; ErrorAction = 'Stop' }
    if ($adminUser) {
        $sessionArgs.Credential = New-Object System.Management.Automation.PSCredential $adminUser, (Get-SecurePassword $adminUser $adminFile)
    }
    $who = if ($adminUser) { $adminUser } else { 'your Windows login' }
    Write-Host "Connecting to $($cfg.Server) over PowerShell remoting as $who" -ForegroundColor Cyan
    try { $script:Session = New-PSSession @sessionArgs } catch {
        Fail (@(
            "Couldn't open a PowerShell remoting session to $($cfg.Server): $($_.Exception.Message)",
            'One-time setup:',
            '  On the server, in an admin PowerShell:',
            '      Enable-PSRemoting -Force -SkipNetworkProfileCheck',
            '  On this PC, in an admin PowerShell (needed when the server is an IP address or not in your domain):',
            "      Start-Service WinRM; Set-Item WSMan:\localhost\Client\TrustedHosts -Value '$($cfg.Server)' -Concatenate -Force",
            "ServiceAdminUserName in $LocalFile must be an administrator on the server. If its password changed, delete $adminFile and run the task again."
        ) -join "`n")
    }
    try {
        $remote = Invoke-Command -Session $script:Session -ScriptBlock {
            $id = [Security.Principal.WindowsIdentity]::GetCurrent()
            [pscustomobject]@{
                Admin = (New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
                Shares = @(Get-SmbShare -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Path = $_.Path } })
            }
        }
    } catch { Fail "Connected to $($cfg.Server), but the remote check failed: $($_.Exception.Message)" }
    if (-not $remote.Admin) {
        Fail "$who isn't elevated on the server. Use an administrator account for ServiceAdminUserName. (Windows strips admin rights from LOCAL accounts over the network unless LocalAccountTokenFilterPolicy is 1 - Enable-PSRemoting sets it on a workgroup PC.)"
    }
    # The server-side folder behind SharePath (\\server\share[\sub]), unless ServerAppPath says otherwise.
    $ServerAppPath = $cfg.ServerAppPath
    if (-not $ServerAppPath) {
        $parts = $Share.TrimStart('\') -split '\\'
        $match = $remote.Shares | Where-Object { $_.Name -eq $parts[1] } | Select-Object -First 1
        if (-not $match) { Fail "The server has no share named '$($parts[1])'. Set ServerAppPath in $LocalFile to the app folder on the server (e.g. C:\inetpub\OmniCard)." }
        $ServerAppPath = (@($match.Path) + @($parts | Select-Object -Skip 2)) -join '\'
    }
    Write-Host "    App folder on the server: $ServerAppPath (service $ServiceName)"
}

# ---- 2. Build the React SPA (dotnet publish does NOT run npm) ----
if ($cfg.BuildSpa) {
    Write-Host 'Building SPA...' -ForegroundColor Cyan
    Push-Location "$Repo\OmniCard.Web\ClientApp"
    npm install
    if ($LASTEXITCODE -ne 0) { Pop-Location; Fail 'npm install FAILED' }
    npm run build
    if ($LASTEXITCODE -ne 0) { Pop-Location; Fail 'SPA build FAILED' }
    Pop-Location
}

# ---- 3. Publish into a clean local staging folder (single RID; see README > Publish & IIS) ----
Write-Host "Publishing to $Staging" -ForegroundColor Cyan
if (Test-Path -LiteralPath $Staging) { Remove-Item -LiteralPath $Staging -Recurse -Force }
dotnet publish "$Repo\OmniCard.Web\OmniCard.Web.csproj" -c Release -r win-x64 --self-contained false -o $Staging
if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish FAILED' }

# ---- 4. Keep the server's hand-filled appsettings.json sections ----
# The repo's appsettings.json supplies any NEW keys; the PreserveSettings sections keep the server's
# values. If the server's copy can't be read, leave the server's file untouched rather than risk
# replacing it with the repo's placeholder.
$StagedSettings = Join-Path $Staging 'appsettings.json'
$SkipAppSettings = $false
if ($ServerHasSettings) {
    Remove-Item -LiteralPath $ServerCopy -ErrorAction SilentlyContinue
    if ($Method -eq 'Share') {
        Copy-Item -LiteralPath (Join-Path $Share 'appsettings.json') -Destination $ServerCopy
    } else {
        $fetch = Join-Path $Temp 'omnicard-msdeploy-fetch.txt'
        Invoke-MsDeploy "-verb:sync -source:contentPath='$($cfg.SiteName)/appsettings.json',$Remote -dest:contentPath='$ServerCopy' -allowUntrusted" $fetch | Out-Null
        Remove-Item -LiteralPath $fetch, "$fetch.err" -ErrorAction SilentlyContinue
    }
    try {
        $published = Get-Content -LiteralPath $StagedSettings -Raw | ConvertFrom-Json
        $saved     = Get-Content -LiteralPath $ServerCopy -Raw | ConvertFrom-Json
        foreach ($key in $cfg.PreserveSettings) {
            if ($saved.PSObject.Properties.Name -contains $key) {
                $published | Add-Member -NotePropertyName $key -NotePropertyValue $saved.$key -Force
            }
        }
        ($published | ConvertTo-Json -Depth 32) | Out-File -LiteralPath $StagedSettings -Encoding utf8
        Write-Host "Keeping the server's appsettings.json sections: $($cfg.PreserveSettings -join ', ')" -ForegroundColor Cyan
    } catch {
        $SkipAppSettings = $true
        Write-Host "Couldn't read/merge the server's appsettings.json ($_). Leaving it untouched - new repo keys won't reach it this time." -ForegroundColor Yellow
    }
    Remove-Item -LiteralPath $ServerCopy -ErrorAction SilentlyContinue
} else {
    Write-Host "The server has no appsettings.json yet - deploying the repo's copy. Edit it on the server (DataDirectory, ConnectionStrings, ...); later deploys keep those sections." -ForegroundColor Yellow
}

# ---- 5. Deploy: replace the site with the staged build, app offline meanwhile ----
# Both paths delete server files the new build no longer has (stale content-hashed SPA chunks would
# otherwise pile up), except the ASP.NET Core Module's logs\ folder.
if ($Method -eq 'Share') {
    # app_offline.htm makes the ASP.NET Core Module stop the app and release its DLL locks. A Windows-
    # service install (install-service.ps1) honors it too: the service stops, and starts again by itself
    # once the file is removed. robocopy's retries cover the app's shutdown either way.
    # -InstallService stops the service outright first instead (install-service.ps1 starts it again), so
    # its restart helper isn't involved; app_offline.htm still covers a first deploy over a live IIS site.
    if ($InstallService) {
        Write-Host "Stopping service $ServiceName on the server" -ForegroundColor Cyan
        try {
            Invoke-Command -Session $script:Session -ArgumentList $ServiceName, $ServerAppPath -ScriptBlock {
                param($name, $dir)
                $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
                if ($svc -and $svc.Status -ne 'Stopped') { Stop-Service -Name $name -Force -ErrorAction Stop }
                # Wait for the process itself to exit so its DLLs are unlocked.
                Get-Process -Name 'OmniCard.Web' -ErrorAction SilentlyContinue |
                    Where-Object { $_.Path -and $_.Path.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) } |
                    Wait-Process -Timeout 60 -ErrorAction SilentlyContinue
            }
        } catch { Fail "Couldn't stop service $ServiceName on the server: $($_.Exception.Message)" }
    }
    $offline = Join-Path $Share 'app_offline.htm'
    Set-Content -LiteralPath $offline -Value '<!doctype html><title>Updating</title><p>OmniCard is being updated. Try again in a minute.</p>'
    Start-Sleep -Seconds 3
    $exclude = @('app_offline.htm')
    if ($SkipAppSettings) { $exclude += 'appsettings.json' }
    Write-Host "Mirroring the build into $Share" -ForegroundColor Cyan
    robocopy $Staging $Share /MIR /XF @exclude /XD logs /R:20 /W:3 /NFL /NDL /NP /NJH
    $code = $LASTEXITCODE   # robocopy: 0-7 = success, 8+ = failure
    if ($code -ge 8) { Fail "robocopy FAILED (exit $code). The site is left offline (app_offline.htm) so nobody hits a half-updated app; run the task again." }
    Remove-Item -LiteralPath $offline

    if ($InstallService) {
        # A child powershell.exe so the server's execution policy doesn't block the script file; its
        # output streams back as it runs, with the exit code as the last line.
        Write-Host "Running install-service.ps1 on the server" -ForegroundColor Cyan
        $exit = 1
        try {
            Invoke-Command -Session $script:Session -ArgumentList $ServerAppPath, $ServiceName -ScriptBlock {
                param($dir, $name)
                & "$env:windir\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass `
                    -File (Join-Path $dir 'install-service.ps1') -ServiceName $name 2>&1 | ForEach-Object { "$_" }
                "EXIT:$LASTEXITCODE"
            } | ForEach-Object {
                if ($_ -match '^EXIT:(-?\d+)$') { $exit = [int]$Matches[1] } else { Write-Host "    $_" }
            }
        } catch { Fail "Couldn't run install-service.ps1 on the server: $($_.Exception.Message)" }
        if ($exit -ne 0) { Fail "install-service.ps1 FAILED on the server (exit $exit) - see its output above. The new build is in place; fix the cause and run the task again." }
    }
} else {
    $skips = "-skip:objectName=dirPath,absolutePath='\\logs$'"
    if ($SkipAppSettings) { $skips += " -skip:objectName=filePath,absolutePath='\\appsettings\.json$'" }
    Write-Host "Syncing the build to '$($cfg.SiteName)' via Web Deploy" -ForegroundColor Cyan
    $code = Invoke-MsDeploy "-verb:sync -source:contentPath='$Staging' -dest:contentPath='$($cfg.SiteName)',$Remote -enableRule:AppOffline -allowUntrusted -retryAttempts:5 -retryInterval:3000 $skips"
    if ($code -ne 0) { Fail "Web Deploy sync FAILED (exit $code)." }
}

Close-Share
Remove-Item -LiteralPath $Staging -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "DONE - $SiteUrl" -ForegroundColor Green
