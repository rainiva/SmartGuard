@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title SmartGuard Tray

taskkill /F /IM SmartGuard.Tray.exe >nul 2>&1
timeout /t 1 /nobreak >nul

if not exist "%~dp0bin\SmartGuard.Tray.exe" (
    echo.
    echo ERROR: SmartGuard.Tray.exe not found.
    echo Run scripts\Publish-Tray.ps1 or reinstall SmartGuard.
    echo.
    pause
    exit /b 1
)

start "" "%~dp0bin\SmartGuard.Tray.exe" --root "%~dp0"
echo.
echo Tray restarted. Check the notification area.
echo.
pause
