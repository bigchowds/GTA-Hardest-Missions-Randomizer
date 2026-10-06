@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Get-GHMRCompatibility.ps1"
echo.
echo Press any key to close this window.
pause >nul
endlocal
