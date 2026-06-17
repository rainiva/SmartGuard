$root = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { 'D:\Project\SmartGuard' }
$taskName = 'SmartGuard Tray'
$trayExe = Join-Path $root 'bin\SmartGuard.Tray.exe'
if (Test-Path -LiteralPath $trayExe) {
    $action = New-ScheduledTaskAction -Execute $trayExe -Argument "--root `"$root`"" -WorkingDirectory $root
}
else {
    $scriptPath = Join-Path $root 'lib\SmartGuard.Tray.ps1'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-WindowStyle Hidden -ExecutionPolicy Bypass -NoProfile -Sta -File `"$scriptPath`"" -WorkingDirectory $root
}
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartInterval (New-TimeSpan -Minutes 1) -RestartCount 999
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
Write-Host "Registered: $taskName"
