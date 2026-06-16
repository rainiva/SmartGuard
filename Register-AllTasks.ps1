# Register Core + Tray scheduled tasks
Write-Host '正在注册核心任务（需管理员）…'
& (Join-Path $PSScriptRoot 'Register-SmartPowerPlanTask.ps1')
Write-Host '正在注册托盘任务…'
& (Join-Path $PSScriptRoot 'Register-TrayTask.ps1')
Write-Host '完成。请注销并重新登录，或手动运行：Start-Tray.cmd'