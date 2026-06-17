@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "CFG=%~1"
if "%CFG%"=="" set "CFG=Release"
set "OUT=%~dp0bin"

echo Publishing SmartGuard desktop suite (%CFG%, framework-dependent win-x64)...

dotnet publish "%~dp0src\SmartGuard.Engine\SmartGuard.Engine.csproj" -c %CFG% -r win-x64 --self-contained false -o "%OUT%"
if errorlevel 1 exit /b 1

dotnet publish "%~dp0src\SmartGuard.Tray\SmartGuard.Tray.csproj" -c %CFG% -r win-x64 --self-contained false -o "%OUT%"
if errorlevel 1 exit /b 1

dotnet publish "%~dp0src\SmartGuard.LogViewer\SmartGuard.LogViewer.csproj" -c %CFG% -r win-x64 --self-contained false -p:PublishReadyToRun=true -o "%OUT%"
if errorlevel 1 exit /b 1

dotnet publish "%~dp0src\SmartGuard.Settings\SmartGuard.Settings.csproj" -c %CFG% -r win-x64 --self-contained false -p:PublishReadyToRun=true -o "%OUT%"
if errorlevel 1 exit /b 1

echo Published to %OUT%
exit /b 0
