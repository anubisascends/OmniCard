# OmniCard - build + provision IIS (HTTPS) + publish (local hosting).
# Run elevated via the "Publish" VS Code task. Machine-specific - edit the settings below to taste.
# One-time hosting prerequisites you handle yourself (see OmniCard.Web/README.md):
#   - .NET 10 Hosting Bundle (ASP.NET Core Module) + Web Deploy installed
#   - SQL Server running + the one-time data migration (OmniCard.DbMigrator)
#   - The app-pool identity ("IIS AppPool\<AppPool>") needs read/write on your DataDirectory
#     (scans/, card-images/, dataprotection-keys/) and access to SQL Server.

$ErrorActionPreference = 'Stop'

# ------------------------------- settings (edit me) -------------------------------
$SiteName       = 'OmniCardWeb'            # IIS site name (matches the publish profile's DeployIisAppPath)
$AppPool        = 'OmniCardWeb'            # IIS application pool
$Port           = 8081                     # HTTPS binding port for the site
$CertExtraNames = @()                       # extra SANs, e.g. @('omnicard.lan','omnicard.home.arpa')
$PhysicalPath   = 'C:\inetpub\OmniCardWeb' # where the published files live
$PublishProfile = 'localhos'              # OmniCard.Web/Properties/PublishProfiles/<name>.pubxml
$SqlServer      = 'localhost'              # SQL Server instance the app connects to (Windows auth)
$DataDirectory  = 'X:\TCG Card Scanner'   # DataDirectory the app uses (scans/, card-images/, keys)
# --------------------------------------------------------------------------------

# The app pool runs as this virtual account (New-WebAppPool default = ApplicationPoolIdentity).
$PoolIdentity = "IIS AppPool\$AppPool"

$Repo = Split-Path -Parent $PSScriptRoot   # repo root (.vscode's parent)

function Pause-Exit($code) { Read-Host 'Press Enter to close'; exit $code }

Import-Module WebAdministration -ErrorAction Stop

# ---- 1a. Self-signed HTTPS cert covering localhost + this machine's hostname + LAN IP ----
# Pick the IPv4 of the adapter that has a default gateway (the real LAN adapter).
$lanIp = (Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
    Select-Object -First 1).IPv4Address.IPAddress
$sans = @('localhost', $env:COMPUTERNAME) + $CertExtraNames
if ($lanIp) { $sans += $lanIp }
$sans = $sans | Where-Object { $_ } | Select-Object -Unique

# Reuse our cert only if it already covers every name above; otherwise (re)create it (e.g. the LAN IP
# changed under DHCP). Matched by friendly name so re-runs don't pile up certificates.
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.FriendlyName -eq 'OmniCard local' } | Select-Object -First 1
$covered = $cert -and -not (@($sans | Where-Object { $_ -notin @($cert.DnsNameList.Unicode) }))
if (-not $covered) {
    if ($cert) { Remove-Item "Cert:\LocalMachine\My\$($cert.Thumbprint)" -Force }
    Write-Host "Creating self-signed certificate for: $($sans -join ', ')" -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -DnsName $sans -CertStoreLocation Cert:\LocalMachine\My -FriendlyName 'OmniCard local'
}
# Trust it (copy to LocalMachine Trusted Root) so this machine's browser doesn't warn.
if (-not (Test-Path "Cert:\LocalMachine\Root\$($cert.Thumbprint)")) {
    Write-Host 'Trusting the certificate (LocalMachine\Root)' -ForegroundColor Cyan
    $root = New-Object System.Security.Cryptography.X509Certificates.X509Store('Root', 'LocalMachine')
    $root.Open('ReadWrite'); $root.Add($cert); $root.Close()
}

# ---- 1b. App pool (No Managed Code for ASP.NET Core) ----
if (-not (Test-Path "IIS:\AppPools\$AppPool")) {
    Write-Host "Creating app pool '$AppPool'" -ForegroundColor Cyan
    New-WebAppPool -Name $AppPool | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$AppPool" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$AppPool" -Name startMode -Value 'AlwaysRunning'

# ---- 1c. Physical path ----
if (-not (Test-Path $PhysicalPath)) { New-Item -ItemType Directory -Path $PhysicalPath -Force | Out-Null }

# ---- 1d. Site + HTTPS binding (idempotent) ----
if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    Write-Host "Creating site '$SiteName' (https:$Port) -> $PhysicalPath" -ForegroundColor Cyan
    New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPool -Port $Port -Ssl | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPool
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath
    if (-not (Get-WebBinding -Name $SiteName -Protocol https -Port $Port -ErrorAction SilentlyContinue)) {
        New-WebBinding -Name $SiteName -Protocol https -Port $Port -IPAddress '*'
    }
}
# Attach the cert to the https binding.
(Get-WebBinding -Name $SiteName -Protocol https -Port $Port).AddSslCertificate($cert.Thumbprint, 'My')

# ---- 1e. Grant the app-pool identity what the app needs at runtime ----
# (a) Site folder: write for ASP.NET Core Module logs, etc.
& icacls $PhysicalPath /grant "${PoolIdentity}:(OI)(CI)M" /T | Out-Null

# (b) Data directory: the app creates card-images/ + dataprotection-keys/ and writes scans there,
#     so the pool identity needs Modify (inheritable) on the data dir.
if (Test-Path $DataDirectory) {
    & icacls $DataDirectory /grant "${PoolIdentity}:(OI)(CI)M" | Out-Null
} else {
    Write-Host "WARNING: DataDirectory '$DataDirectory' not found - the app will fail to start until it exists and the pool identity can write to it." -ForegroundColor Yellow
}

# (c) SQL Server login: startup runs EF Migrate/EnsureCreated over Windows auth, so the pool identity
#     needs a login + rights on the OmniCard databases (unified store + per-game catalogs). dbcreator
#     lets it create any catalog DB that doesn't exist yet; db_owner on the existing ones.
Write-Host "Granting SQL Server access to $PoolIdentity" -ForegroundColor Cyan
$sqlLogin = "IIS APPPOOL\$AppPool"
$tsql = @"
IF SUSER_ID(N'$sqlLogin') IS NULL CREATE LOGIN [$sqlLogin] FROM WINDOWS;
IF IS_SRVROLEMEMBER('dbcreator', N'$sqlLogin') = 0 ALTER SERVER ROLE dbcreator ADD MEMBER [$sqlLogin];
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'USE ' + QUOTENAME(name) + N'; IF DATABASE_PRINCIPAL_ID(''$sqlLogin'') IS NULL CREATE USER [$sqlLogin] FOR LOGIN [$sqlLogin]; ALTER ROLE db_owner ADD MEMBER [$sqlLogin];'
FROM sys.databases WHERE name = 'OmniCard' OR name LIKE 'OmniCard[_]%';
EXEC sp_executesql @sql;
"@
$sqlFile = Join-Path $env:TEMP 'omnicard-grant.sql'
$tsql | Out-File -FilePath $sqlFile -Encoding ascii
& sqlcmd -S $SqlServer -E -b -i $sqlFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "WARNING: SQL grant failed (are you a SQL sysadmin?). Grant '$sqlLogin' access to the OmniCard databases manually." -ForegroundColor Yellow
}
Remove-Item $sqlFile -ErrorAction SilentlyContinue

# ---- 2. Build the React SPA (dotnet publish does NOT run npm) ----
Write-Host 'Building SPA...' -ForegroundColor Cyan
Push-Location "$Repo\OmniCard.Web\ClientApp"
npm install
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host 'npm install FAILED' -ForegroundColor Red; Pause-Exit 1 }
npm run build
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host 'SPA build FAILED' -ForegroundColor Red; Pause-Exit 1 }
Pop-Location

# ---- 2b. Clean the deploy folder so stale files don't accumulate ----
# The publish profile sets SkipExtraFilesOnServer, so Web Deploy never removes files that a previous
# build left behind - content-hashed SPA chunks (each build drops a fresh ~10 MB opencv asset) pile up
# and the folder balloons over time. Wipe it before publishing. The app's runtime data lives in
# $DataDirectory, not here, so the site folder only holds regenerated build output + logs.
# Safety guard: never let a mis-edited setting turn this into a drive-root wipe.
if ([string]::IsNullOrWhiteSpace($PhysicalPath) -or $PhysicalPath -match '^[A-Za-z]:\\?$') {
    Write-Host "Refusing to clean unsafe PhysicalPath '$PhysicalPath'" -ForegroundColor Red; Pause-Exit 1
}
# Preserve the server-specific sections of the live appsettings.json (these are hand-filled on the
# server and must NOT be reset each deploy). Back up the whole file before the wipe; after publish we
# merge just these top-level sections back over the repo's placeholder so NEW repo keys still flow
# through while the hand-filled values survive.
$PreserveSections = @('DataDirectory', 'ConnectionStrings', 'eBay', 'Mcp')
$LiveAppSettings = Join-Path $PhysicalPath 'appsettings.json'
$AppSettingsBackup = $null
if (Test-Path $LiveAppSettings) {
    $AppSettingsBackup = Join-Path $env:TEMP 'omnicard-appsettings.backup.json'
    Copy-Item -LiteralPath $LiveAppSettings -Destination $AppSettingsBackup -Force
    Write-Host "Preserving server appsettings.json sections: $($PreserveSections -join ', ')" -ForegroundColor Cyan
}
if (Test-Path $PhysicalPath) {
    # Stop the app pool first so the running worker releases its lock on the DLLs, then wait for it to
    # actually exit before deleting (a 'Stopped' state can briefly precede w3wp unloading).
    if (Test-Path "IIS:\AppPools\$AppPool") {
        Write-Host "Stopping app pool '$AppPool' to release file locks" -ForegroundColor Cyan
        if ((Get-WebAppPoolState -Name $AppPool).Value -ne 'Stopped') { Stop-WebAppPool -Name $AppPool }
        for ($i = 0; $i -lt 40 -and (Get-WebAppPoolState -Name $AppPool).Value -ne 'Stopped'; $i++) { Start-Sleep -Milliseconds 250 }
    }
    Write-Host "Cleaning deploy folder $PhysicalPath" -ForegroundColor Cyan
    # Remove folder *contents* (keep the folder so its IIS physical path + ACL grant survive). Retry a
    # couple of times in case a handle is slow to release.
    for ($try = 1; $try -le 3; $try++) {
        try {
            Get-ChildItem -LiteralPath $PhysicalPath -Force | Remove-Item -Recurse -Force -ErrorAction Stop
            break
        } catch {
            if ($try -eq 3) { Write-Host "Could not fully clean '$PhysicalPath': $_" -ForegroundColor Red; Pause-Exit 1 }
            Start-Sleep -Milliseconds 500
        }
    }
}

# ---- 3. Publish to IIS via the Web Deploy profile (AppOffline handles the DLL lock) ----
Write-Host 'Publishing to IIS...' -ForegroundColor Cyan
Push-Location $Repo
dotnet publish OmniCard.Web/OmniCard.Web.csproj -c Release "/p:PublishProfile=$PublishProfile"
$code = $LASTEXITCODE
Pop-Location

# Merge the preserved server sections back over the placeholder appsettings.json publish just wrote:
# the repo file provides any NEW keys, while $PreserveSections keep their hand-filled server values.
# Only on a successful publish (a failed publish leaves the folder half-written - don't touch config).
if ($code -eq 0 -and $AppSettingsBackup -and (Test-Path $AppSettingsBackup) -and (Test-Path $LiveAppSettings)) {
    try {
        $published = Get-Content -LiteralPath $LiveAppSettings -Raw | ConvertFrom-Json
        $saved     = Get-Content -LiteralPath $AppSettingsBackup -Raw | ConvertFrom-Json
        foreach ($key in $PreserveSections) {
            if ($saved.PSObject.Properties.Name -contains $key) {
                if ($published.PSObject.Properties.Name -contains $key) {
                    $published.$key = $saved.$key
                } else {
                    $published | Add-Member -NotePropertyName $key -NotePropertyValue $saved.$key -Force
                }
            }
        }
        ($published | ConvertTo-Json -Depth 32) | Out-File -LiteralPath $LiveAppSettings -Encoding utf8
        Remove-Item -LiteralPath $AppSettingsBackup -ErrorAction SilentlyContinue
        Write-Host "Merged preserved sections into appsettings.json: $($PreserveSections -join ', ')" -ForegroundColor Cyan
    } catch {
        # Never lose the config: on any merge error, fall back to the full preserved file and keep the
        # backup around so nothing hand-filled is lost.
        Copy-Item -LiteralPath $AppSettingsBackup -Destination $LiveAppSettings -Force
        Write-Host "appsettings.json merge failed ($_); restored full preserved copy. Backup kept at $AppSettingsBackup" -ForegroundColor Yellow
    }
}

# Bring the pool back up (we stopped it above; a manually stopped pool stays stopped otherwise).
if (Test-Path "IIS:\AppPools\$AppPool") {
    if ((Get-WebAppPoolState -Name $AppPool).Value -ne 'Started') { Start-WebAppPool -Name $AppPool }
}

if ($code -ne 0) { Write-Host 'PUBLISH FAILED' -ForegroundColor Red; Pause-Exit $code }
Write-Host "DONE - https://localhost:$Port" -ForegroundColor Green
if ($lanIp) { Write-Host "  LAN:  https://${lanIp}:$Port  (other devices must trust the cert - see note)" -ForegroundColor Green }
Pause-Exit 0
