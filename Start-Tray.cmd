@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
REM Dev helper: starts Tray directly. Production uses scheduled task SmartGuard Tray.
"%~dp0bin\SmartGuard.Cli.exe" start tray --root "%~dp0"
set ERR=%ERRORLEVEL%
if %ERR% neq 0 pause
exit /b %ERR%
