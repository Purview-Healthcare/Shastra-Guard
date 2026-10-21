# Removes Shastra Guard from this computer. Windows + L goes back to normal.
$user = (Get-CimInstance Win32_ComputerSystem).UserName
schtasks /end /tn 'Shastra Guard' 2>$null | Out-Null
schtasks /end /tn 'Shastra Guard Keepalive' 2>$null | Out-Null
Get-Process ShastraGuard -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
if ($user) {
    $sid = (New-Object Security.Principal.NTAccount($user)).Translate([Security.Principal.SecurityIdentifier]).Value
    $pol = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    if (Test-Path $pol) { Remove-ItemProperty -Path $pol -Name DisableLockWorkstation -ErrorAction SilentlyContinue }
}
schtasks /delete /tn 'Shastra Guard' /f 2>$null | Out-Null
schtasks /delete /tn 'Shastra Guard Keepalive' /f 2>$null | Out-Null
schtasks /delete /tn 'Shastra Punch In' /f 2>$null | Out-Null
Remove-Item 'C:\Program Files\Purview Shastra Guard' -Recurse -Force -ErrorAction SilentlyContinue
if ($user) {
    $userProfile = (Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid }).LocalPath
    if ($userProfile) { Remove-Item (Join-Path $userProfile 'AppData\Local\Purview Shastra Guard') -Recurse -Force -ErrorAction SilentlyContinue }
}
Write-Host ''
Write-Host 'Shastra Guard has been removed. Windows + L works normally again.' -ForegroundColor Green
Write-Host ''
