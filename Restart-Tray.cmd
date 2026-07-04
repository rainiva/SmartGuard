@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
REM Dev helper: restarts Tray only. For full stop use bin\SmartGuard.Engine.exe --uninstall --root "%~dp0"
"%~dp0bin\SmartGuard.Cli.exe" restart tray --root "%~dp0"
set ERR=%ERRORLEVEL%
if %ERR% neq 0 pause
exit /b %ERR%
