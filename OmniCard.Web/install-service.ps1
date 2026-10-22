# OmniCard - run the web app as a Windows service on its own web server (Kestrel) instead of IIS.
#
# Why: IIS on a Windows CLIENT OS (Windows 10/11 Pro) runs at most 10 requests at once, so a page of card
# images queues behind itself. Kestrel has no such cap. Windows Server's IIS has no cap either, so this
# is mainly for client-OS hosts.
#
# Run it ON THE SERVER, from the deployed app folder, in an elevated PowerShell:
#   powershell -ExecutionPolicy Bypass -File .\install-service.ps1
#   powershell -ExecutionPolicy Bypass -File .\install-service.ps1 -Uninstall   # back to IIS
#
# What it does (safe to re-run; it updates an existing install):
#   - finds the IIS site that serves this folder (or -IisSiteName) and reuses its ports and HTTPS certificate
#     (or takes -HttpPort/-HttpsPort/-CertificateThumbprint)
#   - stops the IIS site + its app pool and turns off their auto-start (so the ports are free)
#   - creates the service under its own virtual account (NT SERVICE\<name>), auto-start, restart on crash,
#     and lets that account restart its own service (needed for app_offline.htm deploys)
#   - grants that account: read on this folder, modify on DataDirectory + watched scan folders, the HTTPS
#     certificate's private key, and db_owner on the OmniCard SQL databases (+ dbcreator)
#   - opens the ports in Windows Firewall for the local subnet, starts the service and checks it answers
#
# Deploys keep working: the remote deploy task's app_offline.htm stops the service, and it starts again
# by itself when the copy is done.

[CmdletBinding()]
param(
    [string]$ServiceName = 'OmniCard',
    [string]$IisSiteName = '',   # default: the IIS site whose folder this script is in
    [int]$HttpPort = 0,
    [int]$HttpsPort = 0,
    [string]$CertificateThumbprint = '',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$AppDir   = $PSScriptRoot
$Exe      = Join-Path $AppDir 'OmniCard.Web.exe'
$Account  = "NT SERVICE\$ServiceName"
$SvcKey   = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$RuleName = "OmniCard ($ServiceName)"

function Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Warn($msg) { Write-Host "    $msg" -ForegroundColor Yellow }
function Fail($msg) { Write-Host $msg -ForegroundColor Red; exit 1 }

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Fail 'Run this from an elevated (Run as administrator) PowerShell.'
}
if (-not (Test-Path -LiteralPath $Exe)) { Fail "OmniCard.Web.exe not found in $AppDir. Run this script from the deployed app folder." }

# The IIS site to take over: the one whose root application serves THIS folder (or -IisSiteName).
# Read from applicationHost.config, which also gives its bindings + app pool. (A 32-bit PowerShell sees
# System32 redirected, so go through Sysnative there.)
$InetSrv = if (Test-Path -LiteralPath "$env:windir\Sysnative") { "$env:windir\Sysnative\inetsrv" } else { "$env:windir\System32\inetsrv" }
$AppCmd    = Join-Path $InetSrv 'appcmd.exe'
$IisConfig = Join-Path $InetSrv 'config\applicationHost.config'
$HasIis    = (Test-Path -LiteralPath $AppCmd) -and (Test-Path -LiteralPath $IisConfig)

function Get-NormalizedPath($p) { [Environment]::ExpandEnvironmentVariables("$p").TrimEnd('\').ToLowerInvariant() }
function Get-IisSites {
    $sites = ([xml](Get-Content -LiteralPath $IisConfig -Raw)).configuration.'system.applicationHost'.sites
    foreach ($site in @($sites.site)) {
        $root = @($site.application) | Where-Object { $_.path -eq '/' } | Select-Object -First 1
        if (-not $root) { continue }
        $vdir = @($root.virtualDirectory) | Where-Object { $_.path -eq '/' } | Select-Object -First 1
        $pool = @($root.applicationPool, $site.applicationDefaults.applicationPool, $sites.applicationDefaults.applicationPool, 'DefaultAppPool') |
            Where-Object { $_ } | Select-Object -First 1
        [pscustomobject]@{
            Name     = $site.name
            Path     = if ($vdir) { [Environment]::ExpandEnvironmentVariables($vdir.physicalPath) } else { '' }
            Pool     = $pool
            Bindings = @($site.bindings.binding | Where-Object { $_ } | ForEach-Object {
                # bindingInformation is ip:port:host
                $ip, $port, $hostName = $_.bindingInformation -split ':', 3
                [pscustomobject]@{ Protocol = $_.protocol; Ip = $ip; Port = [int]$port; Host = $hostName; Sni = ([int]$_.sslFlags -band 1) -eq 1 }
            } | Where-Object { $_.Protocol -in 'http', 'https' })
        }
    }
}

$IisSite = $null
if ($HasIis) {
    $allSites = @(Get-IisSites)
    $IisSite = if ($IisSiteName) { $allSites | Where-Object { $_.Name -eq $IisSiteName } | Select-Object -First 1 }
               else { $allSites | Where-Object { (Get-NormalizedPath $_.Path) -eq (Get-NormalizedPath $AppDir) } | Select-Object -First 1 }
    if ($IisSiteName -and -not $IisSite) { Fail "There's no IIS site named '$IisSiteName'." }
}
$IisSiteName = if ($IisSite) { $IisSite.Name } else { '' }

# ------------------------------------------------------------------------------------------------
if ($Uninstall) {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Step "Removing service $ServiceName"
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
    }
    Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    if ($IisSite) {
        Step "Re-enabling IIS site $IisSiteName"
        $pool = $IisSite.Pool
        & $AppCmd set apppool "/apppool.name:$pool" /autoStart:true /startMode:AlwaysRunning | Out-Null
        & $AppCmd start apppool "/apppool.name:$pool" 2>$null | Out-Null
        & $AppCmd set site "/site.name:$IisSiteName" /serverAutoStart:true | Out-Null
        & $AppCmd start site "/site.name:$IisSiteName" | Out-Null
    }
    Write-Host 'Done. (The folder, certificate and SQL grants for the service account were left in place.)' -ForegroundColor Green
    exit 0
}

# ---- 1. Settings: ports + certificate (from the IIS site unless given), data dir, SQL ----
$settings = Get-Content -LiteralPath (Join-Path $AppDir 'appsettings.json') -Raw | ConvertFrom-Json
$DataDir  = $settings.DataDirectory
$ConnStr  = $settings.ConnectionStrings.OmniCard

$HttpsBinding = $null
if ($IisSite) {
    Step "Taking over IIS site '$($IisSite.Name)' ($($IisSite.Path), app pool $($IisSite.Pool))"
    if (-not $HttpPort -and -not $HttpsPort) {
        $HttpPort     = ($IisSite.Bindings | Where-Object { $_.Protocol -eq 'http' } | Select-Object -First 1).Port
        $HttpsBinding = $IisSite.Bindings | Where-Object { $_.Protocol -eq 'https' } | Select-Object -First 1
        $HttpsPort    = $HttpsBinding.Port
        Write-Host "    Using its ports: http $(if ($HttpPort) { $HttpPort } else { '-' }), https $(if ($HttpsPort) { $HttpsPort } else { '-' })"
    }
} elseif ($HasIis -and -not $HttpPort -and -not $HttpsPort) {
    Write-Host "No IIS site serves $AppDir. Sites found:" -ForegroundColor Red
    $allSites | ForEach-Object { Write-Host "    $($_.Name): $($_.Path)  [$(($_.Bindings | ForEach-Object { "$($_.Protocol):$($_.Port)" }) -join ', ')]" }
    Fail "Run this script from the site's folder, or pass -IisSiteName, or -HttpPort/-HttpsPort to listen without taking over a site."
}
if (-not $HttpPort -and -not $HttpsPort) { $HttpPort = 5000; Warn 'IIS is not installed and no ports were given - listening on http port 5000.' }

$Cert = $null
if ($HttpsPort) {
    if (-not $CertificateThumbprint) {
        # Where IIS registered the binding's certificate with http.sys: by host name (SNI), else by IP.
        $lookups = @()
        if ($HttpsBinding -and $HttpsBinding.Sni -and $HttpsBinding.Host) { $lookups += "hostnameport=$($HttpsBinding.Host):$HttpsPort" }
        if ($HttpsBinding -and $HttpsBinding.Ip -and $HttpsBinding.Ip -ne '*') { $lookups += "ipport=$($HttpsBinding.Ip):$HttpsPort" }
        $lookups += "ipport=0.0.0.0:$HttpsPort", "ipport=[::]:$HttpsPort"
        foreach ($lookup in $lookups) {
            $sslcert = netsh http show sslcert $lookup 2>$null | Out-String
            if ($sslcert -match 'Certificate Hash\s*:\s*([0-9a-fA-F]+)') { $CertificateThumbprint = $Matches[1]; break }
        }
    }
    if (-not $CertificateThumbprint) {
        Fail "No HTTPS certificate is bound to port $HttpsPort. Pass -CertificateThumbprint (a certificate in LocalMachine\My), or -HttpsPort 0 for http only."
    }
    $CertificateThumbprint = ($CertificateThumbprint -replace '[^0-9a-fA-F]', '').ToUpperInvariant()
    $Cert = Get-Item -LiteralPath "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    if (-not $Cert) { Fail "Certificate $CertificateThumbprint isn't in LocalMachine\My." }
    if (-not $Cert.HasPrivateKey) { Fail "Certificate $CertificateThumbprint has no private key." }
    Step "HTTPS certificate: $($Cert.Subject) ($CertificateThumbprint), expires $($Cert.NotAfter.ToString('yyyy-MM-dd'))"
}

# ---- 2. Stop IIS for this site so the ports are free ----
if ($IisSite) {
    Step "Stopping IIS site $IisSiteName and turning off its auto-start"
    & $AppCmd stop site "/site.name:$IisSiteName" 2>$null | Out-Null
    & $AppCmd set site "/site.name:$IisSiteName" /serverAutoStart:false | Out-Null
    $pool = $IisSite.Pool
    & $AppCmd stop apppool "/apppool.name:$pool" 2>$null | Out-Null
    & $AppCmd set apppool "/apppool.name:$pool" /autoStart:false /startMode:OnDemand | Out-Null
    Start-Sleep -Seconds 2
}
# Stop a previous install of the service too, then make sure nothing else holds the ports (IIS shows
# up as 'System', i.e. http.sys - another site bound to the same port).
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; Start-Sleep -Seconds 2 }
foreach ($port in @($HttpPort, $HttpsPort) | Where-Object { $_ }) {
    $listener = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) {
        $owner = (Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue).ProcessName
        Fail "Port $port is still in use by '$owner' (PID $($listener.OwningProcess)), so the service couldn't listen on it. If that's IIS ('System'), another site is bound to port $port - stop it or pick other ports (-HttpPort/-HttpsPort)."
    }
}

# ---- 3. Create or update the service ----
if ($existing) {
    Step "Updating service $ServiceName"
    Set-ItemProperty -LiteralPath $SvcKey -Name ImagePath -Value "`"$Exe`""
} else {
    Step "Creating service $ServiceName"
    New-Service -Name $ServiceName -BinaryPathName "`"$Exe`"" -DisplayName 'OmniCard' `
        -Description 'OmniCard web app (Kestrel). Installed by install-service.ps1.' -StartupType Automatic | Out-Null
}
sc.exe config $ServiceName obj= $Account start= delayed-auto | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "Couldn't set the service account to $Account." }
# Restart after a crash: 1 minute, every time (the failure count resets daily).
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# Start after a LOCAL SQL Server instance named in the connection string.
$sqlHost = $null; $sqlInstance = $null
if ($ConnStr -match '(?i)(?:^|;)\s*(?:Server|Data Source)\s*=\s*([^;]+)') {
    $server = $Matches[1].Trim() -replace '^(?i)tcp:', '' -replace ',\d+$', ''
    $sqlHost, $sqlInstance = $server -split '\\', 2
}
$sqlIsLocal = $sqlHost -and ($sqlHost -in @('.', 'localhost', '(local)', '127.0.0.1', $env:COMPUTERNAME))
if ($sqlIsLocal) {
    $sqlService = if ($sqlInstance) { 'MSSQL$' + $sqlInstance.ToUpperInvariant() } else { 'MSSQLSERVER' }
    if (Get-Service -Name $sqlService -ErrorAction SilentlyContinue) { sc.exe config $ServiceName depend= $sqlService | Out-Null }
}

# Settings for the app, kept with the service (not in appsettings.json, which deploys rewrite).
$environment = @("WindowsService__Name=$ServiceName")
if ($HttpPort)  { $environment += "Kestrel__Endpoints__Http__Url=http://*:$HttpPort" }
if ($HttpsPort) {
    $environment += "Kestrel__Endpoints__Https__Url=https://*:$HttpsPort"
    $environment += "HttpsCertificateThumbprint=$CertificateThumbprint"
}
New-ItemProperty -LiteralPath $SvcKey -Name Environment -PropertyType MultiString -Value $environment -Force | Out-Null

# Let the service account start/stop its own service (the app_offline.htm restart helper runs as it).
$sid = (New-Object Security.Principal.NTAccount $Account).Translate([Security.Principal.SecurityIdentifier]).Value
$sddl = (sc.exe sdshow $ServiceName | Where-Object { $_ -match 'D:' } | Select-Object -First 1).Trim()
if ($sddl -and $sddl -notmatch [regex]::Escape($sid)) {
    $ace = "(A;;RPWPLCLO;;;$sid)"
    $at = $sddl.IndexOf('S:')
    $sddl = if ($at -ge 0) { $sddl.Insert($at, $ace) } else { $sddl + $ace }
    sc.exe sdset $ServiceName $sddl | Out-Null
    if ($LASTEXITCODE -ne 0) { Warn "Couldn't let $Account restart its own service - after a deploy, start it by hand." }
}

# ---- 4. File access ----
Step "Granting $Account access to the app folder and data"
icacls $AppDir /grant "${Account}:(OI)(CI)RX" /Q | Out-Null
if ($DataDir) {
    if (Test-Path -LiteralPath $DataDir) { icacls $DataDir /grant "${Account}:(OI)(CI)M" /Q | Out-Null }
    else { Warn "DataDirectory $DataDir doesn't exist yet." }
    $folderSettings = Join-Path $DataDir 'scan-folder-settings.json'
    if (Test-Path -LiteralPath $folderSettings) {
        foreach ($f in (Get-Content -LiteralPath $folderSettings -Raw | ConvertFrom-Json).Folders) {
            if ($f.Path -and (Test-Path -LiteralPath $f.Path)) { icacls $f.Path /grant "${Account}:(OI)(CI)M" /Q | Out-Null }
            elseif ($f.Path) { Warn "Watched folder $($f.Path) not found - grant $Account Modify on it once it exists." }
        }
    }
} else { Warn 'appsettings.json has no DataDirectory.' }

# The HTTPS certificate's private key (CNG keys live under Crypto\Keys, legacy CAPI keys under MachineKeys).
if ($Cert) {
    $keyFile = $null
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Cert)
    if ($rsa -is [Security.Cryptography.RSACng]) {
        $name = $rsa.Key.UniqueName
        $keyFile = @("$env:ProgramData\Microsoft\Crypto\Keys\$name", "$env:ProgramData\Microsoft\Crypto\SystemKeys\$name",
                     "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys\$name") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    } elseif ($rsa) {
        $name = $Cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
        $keyFile = "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys\$name"
    }
    if ($keyFile -and (Test-Path -LiteralPath $keyFile)) { icacls $keyFile /grant "${Account}:R" /Q | Out-Null }
    else { Warn "Couldn't find the certificate's private key file - grant $Account read on it (certlm.msc > Manage Private Keys)." }
}

# ---- 5. SQL Server access (Windows auth to a local instance only) ----
$integrated = $ConnStr -match '(?i)(Trusted_Connection|Integrated Security)\s*=\s*(true|yes|sspi)'
if ($integrated -and $sqlIsLocal) {
    Step "Granting $Account access to SQL Server ($($sqlHost)$(if ($sqlInstance) { '\' + $sqlInstance }))"
    $csb = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnStr
    $csb['Initial Catalog'] = 'master'
    $conn = New-Object System.Data.SqlClient.SqlConnection $csb.ConnectionString
    try {
        $conn.Open()
        function Exec($sql) { $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; [void]$cmd.ExecuteNonQuery() }
        $login = "[$Account]"
        Exec "IF SUSER_ID(N'$Account') IS NULL CREATE LOGIN $login FROM WINDOWS"
        Exec "ALTER SERVER ROLE dbcreator ADD MEMBER $login"
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT name FROM sys.databases WHERE name = 'OmniCard' OR name LIKE 'OmniCard[_]%'"
        $reader = $cmd.ExecuteReader(); $dbs = @(); while ($reader.Read()) { $dbs += $reader.GetString(0) }; $reader.Close()
        foreach ($db in $dbs) {
            Exec "USE [$db]; IF USER_ID(N'$Account') IS NULL CREATE USER $login FOR LOGIN $login; ALTER ROLE db_owner ADD MEMBER $login;"
        }
        Write-Host "    db_owner on: $($dbs -join ', ')"
    } catch {
        Warn "SQL grants failed ($($_.Exception.Message)). Give $Account a login with db_owner on the OmniCard databases yourself."
    } finally { $conn.Dispose() }
} elseif ($integrated) {
    Warn "SQL Server isn't on this machine: the service connects as this computer's account (DOMAIN\$env:COMPUTERNAME$) - grant that, or use a SQL login."
}

# ---- 6. Firewall: the ports, from the local subnet only ----
Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$ports = @($HttpPort, $HttpsPort) | Where-Object { $_ } | ForEach-Object { "$_" }
New-NetFirewallRule -DisplayName $RuleName -Direction Inbound -Protocol TCP -LocalPort $ports -Program $Exe `
    -RemoteAddress LocalSubnet -Action Allow -Profile Any | Out-Null

# ---- 7. Start + check ----
Step "Starting $ServiceName"
Remove-Item -LiteralPath (Join-Path $AppDir 'app_offline.htm') -ErrorAction SilentlyContinue
Start-Service -Name $ServiceName
$probe = if ($HttpPort) { "http://localhost:$HttpPort/app/" } else { $null }
$ok = $false
for ($i = 0; $i -lt 30 -and -not $ok; $i++) {
    Start-Sleep -Seconds 2
    if ((Get-Service -Name $ServiceName).Status -eq 'Stopped') { break }
    if (-not $probe) { $ok = $null -ne (Get-NetTCPConnection -State Listen -LocalPort $HttpsPort -ErrorAction SilentlyContinue); continue }
    try { $ok = (Invoke-WebRequest $probe -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { }
}
if (-not $ok) {
    Write-Host "The service didn't come up. Recent errors from the Application event log:" -ForegroundColor Red
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Level = 2; StartTime = (Get-Date).AddMinutes(-3) } -MaxEvents 5 -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host "[$($_.ProviderName)] $($_.Message)" }
    Write-Host "Fix the cause and re-run this script, or run it with -Uninstall to go back to IIS." -ForegroundColor Red
    exit 1
}
$urls = @()
if ($HttpPort)  { $urls += "http://${env:COMPUTERNAME}:$HttpPort/app" }
if ($HttpsPort) { $urls += "https://${env:COMPUTERNAME}:$HttpsPort/app" }
Write-Host "OmniCard is running as service '$ServiceName': $($urls -join '  ')" -ForegroundColor Green
