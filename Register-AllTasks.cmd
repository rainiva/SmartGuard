@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title SmartGuard Register Tasks

set "ENGINE=%~dp0bin\SmartGuard.Engine.exe"
if not exist "%ENGINE%" (
    echo.
    echo ERROR: SmartGuard.Engine.exe not found at %ENGINE%
    echo Run scripts\Publish-Engine.ps1 or reinstall SmartGuard.
    echo.
    pause
    exit /b 1
)

echo Registering scheduled tasks (admin required)...
"%ENGINE%" --root "%~dp0" --install --skip-publish
set ERR=%ERRORLEVEL%
if %ERR% neq 0 (
    echo.
    echo Task registration failed with exit code %ERR%
    echo.
    pause
    exit /b %ERR%
)

echo.
echo Done. Log off and log on, or run Start-Tray.cmd manually.
echo.
pause
exit /b 0
