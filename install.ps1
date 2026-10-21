# Shastra Guard one-line installer. Run from a PowerShell window opened as administrator:
#   irm https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/install.ps1 | iex
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$base = 'https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/'
$tmp  = Join-Path $env:TEMP 'ShastraGuardSetup'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host ''
    Write-Host 'This needs an administrator PowerShell window.' -ForegroundColor Red
    Write-Host 'Close this window. Click Start, type PowerShell, right-click "Windows PowerShell" and choose "Run as administrator". Then paste the command again.'
    Write-Host ''
    return
}
Write-Host 'Downloading Shastra Guard...'
foreach ($f in 'ShastraGuard.cs', 'Install-ShastraGuard.ps1') {
    Invoke-WebRequest -UseBasicParsing -Uri ($base + $f + '?t=' + (Get-Date).Ticks) -OutFile (Join-Path $tmp $f)
}
# Run in a separate PowerShell that allows this script, so no setting on the computer is changed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $tmp 'Install-ShastraGuard.ps1')
