@echo off
pushd "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Custom.ps1" %*
set "builder_exit=%errorlevel%"
popd
pause
exit /b %builder_exit%
