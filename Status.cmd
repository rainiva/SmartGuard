@echo off
setlocal
cd /d "%~dp0"
echo === SmartPowerPlan Status ===
echo.
schtasks /Query /TN "SmartPowerPlan Guardian" /FO LIST 2>nul | findstr /I "TaskName Status"
schtasks /Query /TN "SmartPowerPlan Tray" /FO LIST 2>nul | findstr /I "TaskName Status"
echo.
if exist SmartPowerPlan.startup.log (
  echo --- Last startup log ---
  powershell -NoProfile -Command "Get-Content 'SmartPowerPlan.startup.log' -Tail 8"
)
echo.
pause