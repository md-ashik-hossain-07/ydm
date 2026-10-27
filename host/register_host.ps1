<#
.SYNOPSIS
Registers the YDM Native Messaging Host in Windows Registry for Google Chrome and Microsoft Edge.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$HostExePath = "",

    [Parameter(Mandatory=$false)]
    [switch]$Unregister
)

$ErrorActionPreference = "Stop"

$chromeKey = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.ydm.downloader"
$edgeKey   = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.ydm.downloader"

if ($Unregister) {
    Write-Host "Unregistering YDM Native Messaging Host..." -ForegroundColor Cyan
    if (Test-Path $chromeKey) {
        Remove-Item -Path $chromeKey -Force -Recurse
        Write-Host "Removed: $chromeKey" -ForegroundColor Green
    }
    if (Test-Path $edgeKey) {
        Remove-Item -Path $edgeKey -Force -Recurse
        Write-Host "Removed: $edgeKey" -ForegroundColor Green
    }
    Write-Host "Unregistration completed." -ForegroundColor Green
    return
}

# Resolve YDM.Host.exe location
if ([string]::IsNullOrWhiteSpace($HostExePath)) {
    $candidatePaths = @(
        (Join-Path $PSScriptRoot "YDM.Host.exe"),
        (Join-Path $PSScriptRoot "..\publish\ui\YDM.Host.exe"),
        (Join-Path $PSScriptRoot "..\publish\host\YDM.Host.exe"),
        (Join-Path $PSScriptRoot "..\src\YDM.Host\bin\Release\net8.0\YDM.Host.exe"),
        (Join-Path $PSScriptRoot "..\src\YDM.Host\bin\Debug\net8.0\YDM.Host.exe")
    )

    foreach ($candidate in $candidatePaths) {
        if (Test-Path $candidate) {
            $HostExePath = (Resolve-Path $candidate).Path
            break
        }
    }
}

if (-not (Test-Path $HostExePath)) {
    Write-Error "YDM.Host.exe was not found. Please provide -HostExePath '<path_to_YDM.Host.exe>' or build the project."
    exit 1
}

$resolvedExe = [System.IO.Path]::GetFullPath($HostExePath)
$manifestPath = Join-Path $PSScriptRoot "com.ydm.downloader.json"

if (-not (Test-Path $manifestPath)) {
    Write-Error "Native messaging manifest 'com.ydm.downloader.json' not found at: $manifestPath"
    exit 1
}

# Update host manifest path
$manifestContent = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifestContent.path = $resolvedExe
$manifestContent | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host "Updated host manifest path to: $resolvedExe"

# Register for Google Chrome
New-Item -Path $chromeKey -Force | Out-Null
Set-ItemProperty -Path $chromeKey -Name "(Default)" -Value $manifestPath
Write-Host "Registered Chrome Native Messaging Host: $chromeKey" -ForegroundColor Green

# Register for Microsoft Edge
New-Item -Path $edgeKey -Force | Out-Null
Set-ItemProperty -Path $edgeKey -Name "(Default)" -Value $manifestPath
Write-Host "Registered Edge Native Messaging Host: $edgeKey" -ForegroundColor Green

Write-Host "YDM Native Messaging Host registered successfully." -ForegroundColor Green
