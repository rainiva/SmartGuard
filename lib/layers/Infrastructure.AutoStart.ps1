# Infrastructure: 开机自启动（计划任务启停）

function Get-SmartPowerPlanScheduledTaskNames {
    return @('SmartPowerPlan Guardian', 'SmartPowerPlan Tray')
}

function Get-SmartPowerPlanAutoStartEnabled {
    try {
        foreach ($name in (Get-SmartPowerPlanScheduledTaskNames)) {
            $t = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
            if (-not $t) { return $false }
            if ($t.State -eq 'Disabled') { return $false }
        }
        return $true
    }
    catch {
        return $false
    }
}

function Set-SmartPowerPlanAutoStart {
    param(
        [bool]$Enabled,
        [string]$ScriptRoot = 'C:\Tools'
    )
    $names = Get-SmartPowerPlanScheduledTaskNames
    foreach ($name in $names) {
        $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        if (-not $task) {
            if ($Enabled) {
                if ($name -like '*Guardian*') {
                    $reg = Join-Path $ScriptRoot 'Register-SmartPowerPlanTask.ps1'
                    if (Test-Path $reg) { & $reg | Out-Null }
                }
                else {
                    $reg = Join-Path $ScriptRoot 'Register-TrayTask.ps1'
                    if (Test-Path $reg) { & $reg | Out-Null }
                }
            }
            continue
        }
        if ($Enabled) {
            Enable-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue | Out-Null
        }
        else {
            Disable-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue | Out-Null
        }
    }
}
