<#
.SYNOPSIS
Registers the YDM Native Messaging Host in Windows Registry for Google Chrome and Microsoft Edge.
#>

param(
    [string]$HostExePath = "$PSScriptRoot\..\src\YDM.Host\bin\Debug\net8.0\YDM.Host.exe"
)

$ErrorActionPreference = "Stop"

$resolvedExe = [System.IO.Path]::GetFullPath($HostExePath)
if (-not (Test-Path $resolvedExe)) {
    Write-Warning "YDM.Host.exe not found at: $resolvedExe. Please build the solution first."
}

$manifestDir = "$PSScriptRoot"
$manifestPath = [System.IO.Path]::Combine($manifestDir, "com.ydm.downloader.json")

$manifestContent = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifestContent.path = $resolvedExe
$manifestContent | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Updated host manifest path to: $resolvedExe"

# Register for Google Chrome
$chromeKey = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.ydm.downloader"
New-Item -Path $chromeKey -Force | Out-Null
Set-ItemProperty -Path $chromeKey -Name "(Default)" -Value $manifestPath
Write-Host "Registered Chrome Native Messaging Host: $chromeKey"

# Register for Microsoft Edge
$edgeKey = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.ydm.downloader"
New-Item -Path $edgeKey -Force | Out-Null
Set-ItemProperty -Path $edgeKey -Name "(Default)" -Value $manifestPath
Write-Host "Registered Edge Native Messaging Host: $edgeKey"

Write-Host "YDM Native Messaging Host registration completed successfully."
