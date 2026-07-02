@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
title SmartGuard Guardian

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting administrator privileges...
    set "_elevate=%temp%\smartguard-elevate-%random%.vbs"
    echo Set UAC = CreateObject("Shell.Application") > "%_elevate%"
    echo UAC.ShellExecute "%~f0", "", "", "runas", 1 >> "%_elevate%"
    cscript //nologo "%_elevate%"
    del "%_elevate%" >nul 2>&1
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
echo [SmartGuard] Starting Guardian scheduled task...
echo.
schtasks /Run /TN "SmartGuard Guardian"
set ERR=%ERRORLEVEL%
if %ERR% neq 0 (
    echo.
    echo ERROR: Failed to start Guardian task. Register tasks first via Register-AllTasks.cmd
    echo For foreground debugging, use Debug-Engine.cmd
    echo.
    pause
    exit /b %ERR%
)

echo Guardian task started.
echo.
pause
exit /b 0
