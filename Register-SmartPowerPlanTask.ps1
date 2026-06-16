#Requires -RunAsAdministrator
$taskName = 'SmartPowerPlan Guardian'
$scriptPath = 'C:\Tools\lib\SmartPowerPlan.Core.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-WindowStyle Hidden -ExecutionPolicy Bypass -NoProfile -File `"$scriptPath`"" -WorkingDirectory 'C:\Tools'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartInterval (New-TimeSpan -Minutes 1) -RestartCount 999
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
Write-Host "Registered: $taskName"
