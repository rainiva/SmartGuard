# Infrastructure: 开机自启动（计划任务启停）

function Get-SmartGuardScheduledTaskNames {
    return @('SmartGuard Guardian', 'SmartGuard Tray')
}

function Get-SmartGuardAutoStartEnabled {
    try {
        foreach ($name in (Get-SmartGuardScheduledTaskNames)) {
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

function Set-SmartGuardAutoStart {
    param(
        [bool]$Enabled,
        [string]$ScriptRoot = 'D:\Project\SmartGuard'
    )
    $names = Get-SmartGuardScheduledTaskNames
    foreach ($name in $names) {
        $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        if (-not $task) {
            if ($Enabled) {
                if ($name -like '*Guardian*') {
                    $reg = Join-Path $ScriptRoot 'Register-SmartGuardTask.ps1'
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
            if ($task.State -eq 'Disabled') {
                Enable-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue | Out-Null
            }
        }
        else {
            if ($task.State -ne 'Disabled') {
                Disable-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue | Out-Null
            }
        }
    }
}

function Test-SmartGuardAutoStartNeedsUpdate {
    param(
        [bool]$Enabled,
        $PreviousEnabled
    )
    if ($null -eq $PreviousEnabled) { return $true }
    return ([bool]$Enabled -ne [bool]$PreviousEnabled)
}
