@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title SmartGuard Tray
if exist "%~dp0bin\SmartGuard.Tray.exe" (
  start "" "%~dp0bin\SmartGuard.Tray.exe" --root "%~dp0"
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Sta -File "%~dp0lib\SmartGuard.Tray.ps1"
)
