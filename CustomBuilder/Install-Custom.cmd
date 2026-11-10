@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Custom.ps1" %*
set "install_exit=%errorlevel%"
pause
exit /b %install_exit%
