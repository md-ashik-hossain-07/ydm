<#
.SYNOPSIS
Unregisters the YDM Native Messaging Host from Windows Registry for Google Chrome and Microsoft Edge.
#>

$scriptPath = Join-Path $PSScriptRoot "register_host.ps1"
& $scriptPath -Unregister
