@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
"%~dp0bin\SmartGuard.Cli.exe" start guardian --root "%~dp0"
set ERR=%ERRORLEVEL%
if %ERR% neq 0 pause
exit /b %ERR%
