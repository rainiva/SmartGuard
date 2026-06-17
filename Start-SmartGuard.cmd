@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"
call "%~dp0Start-Core.cmd"
