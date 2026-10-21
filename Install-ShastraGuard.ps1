# Installs Shastra Guard for the person currently signed in to this computer.
# Run through "Install Shastra Guard (run as admin).cmd".
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$ErrorActionPreference = 'Continue'
$dir  = $null
$exe  = $null
$net  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
function Say($m, $c = 'White') { Write-Host $m -ForegroundColor $c }

try {
    # 1. Who is this for?
    $user = (Get-CimInstance Win32_ComputerSystem).UserName
    if (-not $user) { throw 'Nobody is signed in. Sign in as the employee first, then run this again.' }
    $sid = (New-Object Security.Principal.NTAccount($user)).Translate([Security.Principal.SecurityIdentifier]).Value
    Say "Installing Shastra Guard for: $user"
    # Install into the employee's own profile, so the program can update itself without admin rights
    $userProfile = (Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid }).LocalPath
    if (-not $userProfile) { $userProfile = (Get-ItemProperty "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid").ProfileImagePath }
    if (-not $userProfile) { throw 'Could not find the signed-in user''s profile folder.' }
    $dir = Join-Path $userProfile 'AppData\Local\Purview Shastra Guard'
    $exe = Join-Path $dir 'ShastraGuard.exe'
    Say "Install folder: $dir"

    # 2. Stop any older copy
    cmd /c 'schtasks /end /tn "Shastra Guard" >nul 2>&1'
    cmd /c 'schtasks /end /tn "Shastra Guard Keepalive" >nul 2>&1'
    cmd /c 'schtasks /end /tn "Shastra Punch In" >nul 2>&1'
    Get-Process ShastraGuard -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    cmd /c 'schtasks /delete /tn "Shastra Guard" /f >nul 2>&1'
    cmd /c 'schtasks /delete /tn "Shastra Guard Keepalive" /f >nul 2>&1'
    Remove-Item 'C:\Program Files\Purview Shastra Guard' -Recurse -Force -ErrorAction SilentlyContinue
    $pol = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    if (Test-Path $pol) { Remove-ItemProperty -Path $pol -Name DisableLockWorkstation -ErrorAction SilentlyContinue }

    # 3. Build the program on this computer
    Say 'Building...'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Remove-Item $exe -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $here 'ShastraGuard.cs') (Join-Path $dir 'ShastraGuard.cs') -Force
    $build = & "$net\csc.exe" /nologo /target:winexe "/out:$exe" "/lib:$net\WPF" /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $here 'ShastraGuard.cs') 2>&1
    if (-not (Test-Path $exe)) { throw ('Build failed: ' + ($build -join ' ')) }

    # 4. Let this user switch Windows + L on and off (needed for the two-press lock)
    if (-not (Test-Path $pol)) { New-Item -Path $pol -Force | Out-Null }
    $acl = Get-Acl $pol
    $acl.AddAccessRule((New-Object Security.AccessControl.RegistryAccessRule($user, 'SetValue, ReadKey', 'None', 'None', 'Allow')))
    Set-Acl -Path $pol -AclObject $acl

    # 5. Start it at login and unlock, and restart it within 1 minute if it ever stops
    function TaskXml($triggers, $arguments) {
    $argLine = ''
    if ($arguments) { $argLine = "<Arguments>$arguments</Arguments>" }
@"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Shastra Guard: shows Purview Shastra at login, unlock and before locking.</Description>
  </RegistrationInfo>
  <Triggers>
$triggers
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$user</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$exe</Command>
      $argLine
    </Exec>
  </Actions>
</Task>
"@
    }
    $main = @"
    <LogonTrigger><Enabled>true</Enabled><UserId>$user</UserId></LogonTrigger>
    <SessionStateChangeTrigger><Enabled>true</Enabled><StateChange>SessionUnlock</StateChange><UserId>$user</UserId></SessionStateChangeTrigger>
"@
    $keep = @"
    <TimeTrigger>
      <Repetition><Interval>PT1M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
      <StartBoundary>2026-01-01T00:00:00</StartBoundary>
      <Enabled>true</Enabled>
    </TimeTrigger>
"@
    cmd /c 'schtasks /delete /tn "Shastra Punch In" /f >nul 2>&1'
    Register-ScheduledTask -TaskName 'Shastra Guard' -Xml (TaskXml $main '') -Force -ErrorAction Stop | Out-Null
    Register-ScheduledTask -TaskName 'Shastra Guard Keepalive' -Xml (TaskXml $keep '--quiet') -Force -ErrorAction Stop | Out-Null
    cmd /c 'schtasks /run /tn "Shastra Guard" >nul 2>&1'

    Say ''
    # Make sure the employee owns the folder, so self-updates can write to it
    $acl2 = Get-Acl $dir
    $acl2.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($user, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')))
    Set-Acl -Path $dir -AclObject $acl2
    Say ''
    Say "DONE. Shastra Guard is installed for $user and running." Green
    Say 'Test it: press Windows + L once (Shastra appears), then again (it locks).'
}
catch {
    Say ''
    Say 'Something went wrong:' Red
    Say $_.Exception.Message
}
Say ''
