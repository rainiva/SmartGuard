@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title SmartGuard Tray

if not exist "%~dp0bin\SmartGuard.Tray.exe" (
    echo.
    echo ERROR: SmartGuard.Tray.exe not found.
    echo Run build.cmd or reinstall SmartGuard.
    echo.
    pause
    exit /b 1
)

start "" "%~dp0bin\SmartGuard.Tray.exe" --root "%~dp0"
