@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

echo === [1/3] Publishing SmartGuard desktop suite ===
call "%~dp0build.cmd" %CFG%
if errorlevel 1 goto fail

echo.
echo === [2/3] Running tests ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-Tests.ps1"
if errorlevel 1 goto fail

echo.
echo === [3/3] Registering scheduled tasks ===
call "%~dp0Register-AllTasks.cmd"
exit /b %ERRORLEVEL%

:fail
echo Setup failed.
pause
exit /b 1
