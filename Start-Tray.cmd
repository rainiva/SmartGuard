@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title ?????? - ??
echo ???????
powershell.exe -NoProfile -ExecutionPolicy Bypass -Sta -File "%~dp0lib\SmartGuard.Tray.ps1"
echo.
echo ??????
pause