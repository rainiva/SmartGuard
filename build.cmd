@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "CFG=%~1"
if "%CFG%"=="" set "CFG=Release"

if exist "%~dp0bin\SmartGuard.Cli.exe" (
  "%~dp0bin\SmartGuard.Cli.exe" publish %CFG% --root "%~dp0"
  exit /b %ERRORLEVEL%
)

dotnet run --project "%~dp0src\SmartGuard.Cli\SmartGuard.Cli.csproj" -c %CFG% --no-build -- publish %CFG% --root "%~dp0"
if errorlevel 1 (
  dotnet run --project "%~dp0src\SmartGuard.Cli\SmartGuard.Cli.csproj" -c %CFG% -- publish %CFG% --root "%~dp0"
)
exit /b %ERRORLEVEL%
