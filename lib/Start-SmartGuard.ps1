# 手动启动守护（管理员 PowerShell）
#Requires -RunAsAdministrator
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'lib\SmartGuard.Core.ps1')
