<#
.SYNOPSIS
Installs YDM (Your Download Manager) from an official GitHub Release over HTTPS with strict SHA-256 integrity verification.

.DESCRIPTION
Queries GitHub Releases for the specified version (or latest), downloads the official Windows installer,
validates its cryptographic SHA-256 hash against the release checksums.txt manifest,
and executes the verified installer.

SECURITY GUARANTEE:
This script strictly enforces checksum matching. If the official checksum manifest is missing
or if the computed hash does not match, execution is aborted immediately and the downloaded file is purged.

.PARAMETER Repository
Required. The GitHub repository in format 'owner/repo' (e.g. 'octocat/ydm').

.PARAMETER Version
The release version tag to install (e.g. 'v1.0.0'). Defaults to 'latest'.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false, Position=0)]
    [string]$Repository = "md-ashik-hossain-07/ydm",

    [Parameter(Mandatory=$false, Position=1)]
    [string]$Version = "latest"
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  YDM (Your Download Manager) Installer   " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Validate repository argument
if ([string]::IsNullOrWhiteSpace($Repository)) {
    $Repository = "md-ashik-hossain-07/ydm"
}

# 2. Query GitHub release endpoint
$releaseApiUrl = if ($Version -eq "latest") {
    "https://api.github.com/repos/$Repository/releases/latest"
} else {
    "https://api.github.com/repos/$Repository/releases/tags/$Version"
}

Write-Host "[1/4] Querying release metadata from GitHub ($Repository, $Version)..."
try {
    $headers = @{ "User-Agent" = "YDM-PowerShell-Installer" }
    $releaseJson = Invoke-RestMethod -Uri $releaseApiUrl -Headers $headers
} catch {
    Write-Error "Failed to retrieve release metadata for '$Repository' ($Version): $_"
    exit 1
}

$releaseTag = $releaseJson.tag_name
Write-Host "Found Release: $releaseTag" -ForegroundColor Green

# 3. Locate release assets
$installerAsset = $releaseJson.assets | Where-Object { $_.name -like "*setup*.exe" } | Select-Object -First 1
$checksumAsset = $releaseJson.assets | Where-Object { $_.name -like "*checksums*.txt" -or $_.name -like "*sha256*.txt" } | Select-Object -First 1

if (-not $installerAsset) {
    Write-Error "Release $releaseTag does not contain a setup executable (*setup*.exe)."
    exit 1
}

if (-not $checksumAsset) {
    Write-Error "Security Error: Checksum manifest (*checksums*.txt) is missing in release $releaseTag. Refusing to download or execute unverified binaries."
    exit 1
}

$tempDir = [System.IO.Path]::Combine($env:TEMP, "YDM_Install_$releaseTag")
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir | Out-Null
}

$installerDest = [System.IO.Path]::Combine($tempDir, $installerAsset.name)
$checksumDest = [System.IO.Path]::Combine($tempDir, $checksumAsset.name)

# 4. Download checksum manifest first
Write-Host "[2/4] Downloading official checksum manifest..."
Invoke-WebRequest -Uri $checksumAsset.browser_download_url -OutFile $checksumDest -UseBasicParsing
$checksumContent = Get-Content $checksumDest -Raw

# 5. Download installer asset
Write-Host "[3/4] Downloading installer: $($installerAsset.name)..."
Invoke-WebRequest -Uri $installerAsset.browser_download_url -OutFile $installerDest -UseBasicParsing

# 6. Verify SHA-256 integrity
Write-Host "[4/4] Verifying SHA-256 cryptographic hash..."
$computedHash = (Get-FileHash -Path $installerDest -Algorithm SHA256).Hash.ToLowerInvariant()

if ($checksumContent -notmatch $computedHash) {
    Remove-Item $installerDest -Force -ErrorAction SilentlyContinue
    Write-Error "SECURITY VERIFICATION FAILED! The computed hash ($computedHash) does not match the official release checksums. The downloaded file has been deleted."
    exit 1
}

Write-Host "Integrity verification passed: SHA-256 match ($computedHash)" -ForegroundColor Green

# 7. Execute verified installer
Write-Host "Launching verified installer..." -ForegroundColor Cyan
Start-Process -FilePath $installerDest -Wait

Write-Host "YDM installation finished successfully." -ForegroundColor Green
