@echo off
setlocal EnableExtensions
set "_target=%~1"
if "%_target%"=="" exit /b 1
set "_elevate=%temp%\smartguard-elevate-%random%.vbs"
echo Set UAC = CreateObject("Shell.Application") > "%_elevate%"
echo UAC.ShellExecute "%_target%", "", "", "runas", 1 >> "%_elevate%"
cscript //nologo "%_elevate%"
del "%_elevate%" >nul 2>&1
exit /b
