# Domain: 幂等决策（计划切换 / 亮度 / 通知 / 日志指纹）

function New-GuardIdempotencyState {
    return @{
        LastAppliedPlanGuid       = $null
        LastAppliedBrightness     = $null
        LastNotificationEventId   = $null
        TickLogs                  = @()
    }
}

function Get-LogMessageFingerprint {
    param([string]$Message)
    if ([string]::IsNullOrWhiteSpace($Message)) { return $null }
    return $Message.Trim().ToLower()
}

function Test-ShouldApplyPowerPlanSwitch {
    param(
        [string]$CurrentGuid,
        [string]$TargetGuid
    )
    if ([string]::IsNullOrWhiteSpace($TargetGuid)) { return $false }
    if ([string]::IsNullOrWhiteSpace($CurrentGuid)) { return $true }
    return ($CurrentGuid.ToLower() -ne $TargetGuid.ToLower())
}

function Test-ShouldApplyBrightnessLevel {
    param(
        [int]$TargetLevel,
        [int]$CurrentLevel
    )
    if ($TargetLevel -lt 0) { return $false }
    if ($CurrentLevel -lt 0) { return $true }
    return ($CurrentLevel -ne $TargetLevel)
}

function Test-ShouldShowStatusNotification {
    param(
        [string]$LastEventId,
        [hashtable]$Event
    )
    if (-not $Event -or [string]::IsNullOrWhiteSpace($Event.id)) { return $false }
    if ([string]::IsNullOrWhiteSpace($LastEventId)) { return $true }
    return ($LastEventId -ne $Event.id)
}

function Test-ShouldWriteLogMessage {
    param(
        [hashtable]$State,
        [string]$Message
    )
    if ([string]::IsNullOrWhiteSpace($Message)) { return $false }
    $fp = Get-LogMessageFingerprint -Message $Message
    if (-not $State.TickLogs) { return $true }
    return ($State.TickLogs -notcontains $fp)
}

function Register-AppliedPowerPlanSwitch {
    param([hashtable]$State, [string]$PlanGuid)
    $State.LastAppliedPlanGuid = $PlanGuid.ToLower()
    return $State
}

function Register-AppliedBrightnessLevel {
    param([hashtable]$State, [int]$Level)
    $State.LastAppliedBrightness = $Level
    return $State
}

function Register-ShownStatusNotification {
    param([hashtable]$State, [string]$EventId)
    $State.LastNotificationEventId = $EventId
    return $State
}

function Register-WrittenLogFingerprint {
    param([hashtable]$State, [string]$Message)
    if (-not $State.TickLogs) { $State.TickLogs = @() }
    $fp = Get-LogMessageFingerprint -Message $Message
    if ($State.TickLogs -notcontains $fp) {
        $State.TickLogs = @($State.TickLogs + $fp)
    }
    return $State
}

function Reset-IdempotencyTickLogs {
    param([hashtable]$State)
    $State.TickLogs = @()
    return $State
}
