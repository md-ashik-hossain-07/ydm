<#
.SYNOPSIS
Installs or updates YDM (Your Download Manager) from official GitHub Releases.

.DESCRIPTION
Downloads the specified or latest release of YDM from GitHub Releases over HTTPS,
verifies the SHA-256 integrity hash against the official checksums.txt,
and launches the installer.

.PARAMETER Version
Version tag to install (e.g. 'v1.0.0'). Defaults to 'latest'.

.PARAMETER Repository
GitHub repository path in format 'owner/repo'. Defaults to 'ydm-downloader/ydm'.
#>

[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$Repository = "ydm-downloader/ydm"
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  YDM (Your Download Manager) Installer   " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Determine release endpoint
$releaseApiUrl = if ($Version -eq "latest") {
    "https://api.github.com/repos/$Repository/releases/latest"
} else {
    "https://api.github.com/repos/$Repository/releases/tags/$Version"
}

Write-Host "[1/4] Querying release metadata from GitHub ($Version)..."
try {
    $headers = @{ "User-Agent" = "YDM-PowerShell-Installer" }
    $releaseJson = Invoke-RestMethod -Uri $releaseApiUrl -Headers $headers
} catch {
    Write-Error "Failed to query release from GitHub: $_"
    exit 1
}

$releaseTag = $releaseJson.tag_name
Write-Host "Target Release: $releaseTag" -ForegroundColor Green

# 2. Locate installer asset and checksum asset
$installerAsset = $releaseJson.assets | Where-Object { $_.name -like "*setup*.exe" } | Select-Object -First 1
$checksumAsset = $releaseJson.assets | Where-Object { $_.name -like "*checksums*.txt" -or $_.name -like "*sha256*.txt" } | Select-Object -First 1

if (-not $installerAsset) {
    Write-Error "Installer executable not found in release assets for $releaseTag."
    exit 1
}

$tempDir = [System.IO.Path]::Combine($env:TEMP, "YDM_Install_$releaseTag")
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir | Out-Null
}

$installerDest = [System.IO.Path]::Combine($tempDir, $installerAsset.name)
Write-Host "[2/4] Downloading installer: $($installerAsset.name)..."
Invoke-WebRequest -Uri $installerAsset.browser_download_url -OutFile $installerDest -UseBasicParsing

# 3. Checksum Verification
if ($checksumAsset) {
    Write-Host "[3/4] Verifying SHA-256 integrity checksum..."
    $checksumDest = [System.IO.Path]::Combine($tempDir, $checksumAsset.name)
    Invoke-WebRequest -Uri $checksumAsset.browser_download_url -OutFile $checksumDest -UseBasicParsing

    $computedHash = (Get-FileHash -Path $installerDest -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumContent = Get-Content $checksumDest -Raw

    if ($checksumContent -notmatch $computedHash) {
        Write-Error "Integrity check failed! Computed hash ($computedHash) does not match official release checksums."
        Remove-Item $installerDest -Force
        exit 1
    }
    Write-Host "Checksum verified successfully: $computedHash" -ForegroundColor Green
} else {
    Write-Host "[3/4] Notice: Official checksum file not present in release. Proceeding with caution." -ForegroundColor Yellow
}

# 4. Launch Installer
Write-Host "[4/4] Launching YDM installer..." -ForegroundColor Cyan
Start-Process -FilePath $installerDest -Wait

Write-Host "Installation completed successfully." -ForegroundColor Green
