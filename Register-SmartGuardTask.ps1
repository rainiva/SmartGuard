#Requires -RunAsAdministrator
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$taskName = 'SmartGuard Guardian'
$exePath = Join-Path $root 'bin\SmartGuard.Engine.exe'
$psFallback = Join-Path $root 'lib\SmartGuard.Core.ps1'

if (Test-Path -LiteralPath $exePath) {
    $action = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $root
    Write-Host "Registering C# engine: $exePath"
}
else {
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-WindowStyle Hidden -ExecutionPolicy Bypass -NoProfile -File `"$psFallback`"" -WorkingDirectory $root
    Write-Host "Engine exe not found; falling back to PowerShell: $psFallback"
}

$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartInterval (New-TimeSpan -Minutes 1) -RestartCount 999
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
Write-Host "Registered: $taskName"
