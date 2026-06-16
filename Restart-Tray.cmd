@echo off
chcp 65001 >nul
cd /d "%~dp0"
title ????
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Restart-Tray.ps1"
echo.
pause