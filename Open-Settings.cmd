@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
"%~dp0bin\SmartGuard.Settings.exe" --root "%~dp0"
