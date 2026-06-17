@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul
cd /d "%~dp0"
echo === SmartGuard Status ===
echo.
schtasks /Query /TN "SmartGuard Guardian" /FO LIST 2>nul | findstr /I "TaskName Status"
schtasks /Query /TN "SmartGuard Tray" /FO LIST 2>nul | findstr /I "TaskName Status"
echo.
if exist SmartGuard.startup.log (
  echo --- Last startup log ---
  set "LOG=SmartGuard.startup.log"
  set /a SKIP=0
  for /f %%a in ('type "!LOG!" ^| find /c /v ""') do set /a SKIP=%%a-8
  if !SKIP! lss 0 set SKIP=0
  more +!SKIP! "!LOG!"
)
echo.
pause
