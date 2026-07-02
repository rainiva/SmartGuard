@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title SmartGuard Engine (dev foreground)

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting administrator privileges...
    call "%~dp0scripts\Request-AdminElevation.cmd" "%~f0"
    exit /b
)

set "ENGINE=%~dp0bin\SmartGuard.Engine.exe"
if not exist "%ENGINE%" (
    echo.
    echo ERROR: SmartGuard.Engine.exe not found.
    echo Run build.cmd, or reinstall SmartGuard.
    echo.
    pause
    exit /b 1
)

echo.
echo [SmartGuard] Starting engine in foreground (dev only)...
echo Production start: use Start-Core.cmd (schtasks /Run Guardian task).
echo.
"%ENGINE%" --root "%~dp0"
set ERR=%ERRORLEVEL%
echo.
echo [SmartGuard] Engine exited with code %ERR%
echo.
pause
exit /b %ERR%
