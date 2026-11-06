@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Shutdown-With-Xb31.ps1" -ForceShutdown
exit /b %ERRORLEVEL%
