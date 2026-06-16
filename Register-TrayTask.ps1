# Register tray scheduled task (user context, no admin)
$taskName = 'SmartGuard Tray'
$scriptPath = 'D:\Project\SmartGuard\lib\SmartGuard.Tray.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-WindowStyle Hidden -ExecutionPolicy Bypass -NoProfile -Sta -File `"$scriptPath`"" -WorkingDirectory 'D:\Project\SmartGuard'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartInterval (New-TimeSpan -Minutes 1) -RestartCount 999
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
Write-Host "Registered: $taskName"