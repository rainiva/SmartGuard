@echo off
setlocal
cd /d "%~dp0"
echo === SmartGuard Status ===
echo.
schtasks /Query /TN "SmartGuard Guardian" /FO LIST 2>nul | findstr /I "TaskName Status"
schtasks /Query /TN "SmartGuard Tray" /FO LIST 2>nul | findstr /I "TaskName Status"
echo.
if exist SmartGuard.startup.log (
  echo --- Last startup log ---
  powershell -NoProfile -Command "Get-Content 'SmartGuard.startup.log' -Tail 8"
)
echo.
pause