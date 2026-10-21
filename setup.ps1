# Shastra Guard one-line installer. Run from a PowerShell window opened as administrator:
#   irm https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/setup.ps1 | iex
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
$stamp = (Get-Date).Ticks
Invoke-WebRequest -UseBasicParsing -Uri ($base + 'ShastraGuard.cs?t=' + $stamp) -OutFile (Join-Path $tmp 'ShastraGuard.cs')
$code = (Invoke-WebRequest -UseBasicParsing -Uri ($base + 'Install-ShastraGuard.ps1?t=' + $stamp)).Content
# Run the installer from memory (no script file), so Windows' script policy does not apply
$code = $code -replace '\$here\s*=\s*Split-Path[^\r\n]*', ('$here = ''' + $tmp + '''')
Invoke-Expression $code
