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

# ---- 3. Publish to IIS via the Web Deploy profile (AppOffline handles the DLL lock) ----
Write-Host 'Publishing to IIS...' -ForegroundColor Cyan
Push-Location $Repo
dotnet publish OmniCard.Web/OmniCard.Web.csproj -c Release "/p:PublishProfile=$PublishProfile"
$code = $LASTEXITCODE
Pop-Location

if ($code -ne 0) { Write-Host 'PUBLISH FAILED' -ForegroundColor Red; Pause-Exit $code }
Write-Host "DONE - https://localhost:$Port" -ForegroundColor Green
if ($lanIp) { Write-Host "  LAN:  https://${lanIp}:$Port  (other devices must trust the cert - see note)" -ForegroundColor Green }
Pause-Exit 0
