@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Clean-Workspace.ps1" %*
exit /b %ERRORLEVEL%
