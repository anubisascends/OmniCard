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
$CertDns        = 'localhost'              # host name the self-signed cert is issued for
$PhysicalPath   = 'C:\inetpub\OmniCardWeb' # where the published files live
$PublishProfile = 'localhos'              # OmniCard.Web/Properties/PublishProfiles/<name>.pubxml
# --------------------------------------------------------------------------------

$Repo = Split-Path -Parent $PSScriptRoot   # repo root (.vscode's parent)

function Pause-Exit($code) { Read-Host 'Press Enter to close'; exit $code }

Import-Module WebAdministration -ErrorAction Stop

# ---- 1a. Self-signed HTTPS certificate (reuse if one already exists) ----
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq "CN=$CertDns" } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating self-signed certificate for $CertDns" -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -DnsName $CertDns -CertStoreLocation Cert:\LocalMachine\My -FriendlyName 'OmniCard local'
}
# Trust it (copy to LocalMachine Trusted Root) so the browser doesn't warn.
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

# App-pool identity needs write to its own site folder (ASP.NET Core Module logs, etc.).
& icacls $PhysicalPath /grant "IIS AppPool\${AppPool}:(OI)(CI)M" /T | Out-Null

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
Pause-Exit 0
