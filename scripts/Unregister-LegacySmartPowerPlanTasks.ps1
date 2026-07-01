#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'LegacyScheduledTaskNames.ps1')
$oldTasks = $Script:LegacySmartPowerPlanTaskNames

Write-Host '=== 停止旧版 SmartPowerPlan 进程 ===' -ForegroundColor Cyan
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -and $_.CommandLine -match 'C:\\Tools\\lib\\SmartPowerPlan'
} | ForEach-Object {
    Write-Host "结束 PID $($_.ProcessId)"
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '=== 卸载计划任务 ===' -ForegroundColor Cyan
foreach ($name in $oldTasks) {
    $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    if (-not $task) {
        Write-Host "[跳过] 不存在: $name" -ForegroundColor DarkGray
        continue
    }
    Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $name -Confirm:$false
    Write-Host "[已删除] $name" -ForegroundColor Green
}

Write-Host ''
Write-Host '=== 验证 ===' -ForegroundColor Cyan
$remaining = @()
foreach ($name in $oldTasks) {
    if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
        $remaining += $name
        Write-Host "[仍存在] $name" -ForegroundColor Red
    }
    else {
        Write-Host "[已清除] $name" -ForegroundColor Green
    }
}

if ($remaining.Count -gt 0) {
    Write-Host ''
    Write-Host '部分任务未能删除，请确认以管理员身份运行。' -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host '旧版 C:\Tools 计划任务已全部卸载。' -ForegroundColor Green
