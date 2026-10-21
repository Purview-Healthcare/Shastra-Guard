# Removes Shastra Guard from this computer. Windows + L goes back to normal.
$user = (Get-CimInstance Win32_ComputerSystem).UserName
cmd /c 'schtasks /end /tn "Shastra Guard" >nul 2>&1'
cmd /c 'schtasks /end /tn "Shastra Guard Keepalive" >nul 2>&1'
Get-Process ShastraGuard -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
if ($user) {
    $sid = (New-Object Security.Principal.NTAccount($user)).Translate([Security.Principal.SecurityIdentifier]).Value
    $pol = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    if (Test-Path $pol) { Remove-ItemProperty -Path $pol -Name DisableLockWorkstation -ErrorAction SilentlyContinue }
}
cmd /c 'schtasks /delete /tn "Shastra Guard" /f >nul 2>&1'
cmd /c 'schtasks /delete /tn "Shastra Guard Keepalive" /f >nul 2>&1'
cmd /c 'schtasks /delete /tn "Shastra Punch In" /f >nul 2>&1'
Remove-Item 'C:\Program Files\Purview Shastra Guard' -Recurse -Force -ErrorAction SilentlyContinue
if ($user) {
    $userProfile = (Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid }).LocalPath
    if ($userProfile) { Remove-Item (Join-Path $userProfile 'AppData\Local\Purview Shastra Guard') -Recurse -Force -ErrorAction SilentlyContinue }
}
Write-Host ''
Write-Host 'Shastra Guard has been removed. Windows + L works normally again.' -ForegroundColor Green
Write-Host ''
