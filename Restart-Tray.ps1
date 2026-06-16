# Restart-Tray.ps1 - 结束旧托盘并启动新版
#Requires -Version 5.1
$root = if ($PSScriptRoot) { $PSScriptRoot } else { 'C:\Tools' }
$trayScript = Join-Path $root 'lib\SmartPowerPlan.Tray.ps1'

Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*SmartPowerPlan.Tray.ps1*' } |
    ForEach-Object {
        Write-Host "结束旧托盘进程 PID=$($_.ProcessId)"
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

Start-Sleep -Milliseconds 600
Start-Process -FilePath 'powershell.exe' -WorkingDirectory $root -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Sta', '-File', $trayScript
)
Write-Host '托盘已重启（中文版）。请在任务栏右下角查看。'