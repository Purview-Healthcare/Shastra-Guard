# Shastra Guard one-line uninstaller. Run from a PowerShell window opened as administrator:
#   irm https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/remove.ps1 | iex
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$code = (Invoke-WebRequest -UseBasicParsing -Uri ('https://raw.githubusercontent.com/Purview-Healthcare/Shastra-Guard/main/Uninstall-ShastraGuard.ps1?t=' + (Get-Date).Ticks)).Content
$ErrorActionPreference = 'Continue'   # messages like 'task not found' must not stop the installer
Invoke-Expression $code
