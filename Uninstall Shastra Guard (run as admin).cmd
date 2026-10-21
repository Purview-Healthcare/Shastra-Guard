@echo off
net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo This needs administrator rights.
  echo Close this window, right-click the file and choose "Run as administrator".
  echo.
  pause
  exit /b
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-ShastraGuard.ps1"
pause
