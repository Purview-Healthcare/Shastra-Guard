# Shastra Guard one-line uninstaller. Run from a PowerShell window opened as administrator:
#   irm https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/uninstall.ps1 | iex
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$tmp = Join-Path $env:TEMP 'ShastraGuardSetup'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Invoke-WebRequest -UseBasicParsing -Uri ('https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/Uninstall-ShastraGuard.ps1?t=' + (Get-Date).Ticks) -OutFile (Join-Path $tmp 'Uninstall-ShastraGuard.ps1')
# Run in a separate PowerShell that allows this script, so no setting on the computer is changed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $tmp 'Uninstall-ShastraGuard.ps1')
