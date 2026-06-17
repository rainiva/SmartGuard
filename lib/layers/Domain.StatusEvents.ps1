# Domain: status notification events (Core publish -> Tray consume)

function New-StatusNotificationEvent {
    param(
        [ValidateSet('plan_switch', 'external_change', 'brightness_change')]
        [string]$Kind,
        [string]$Title,
        [string]$Body
    )
    return @{
        id    = [guid]::NewGuid().ToString('N')
        kind  = $Kind
        title = $Title
        body  = $Body
        at    = (Get-Date).ToString('s')
    }
}

function Format-PlanSwitchNotification {
    param(
        [string]$PlanName,
        [int]$Brightness,
        [int]$BrightnessBefore = -1
    )
    $title = [string][char]0x7535 + [char]0x6E90 + [char]0x8BA1 + [char]0x5212 + [char]0x5DF2 + [char]0x5207 + [char]0x6362
    if ($BrightnessBefore -ge 0 -and $BrightnessBefore -ne $Brightness) {
        $body = ('{0} [{1}] {2} {3}% -> {4}%' -f (Get-PlanSwitchNotifyPrefix), $PlanName, (Get-BrightnessLabel), $BrightnessBefore, $Brightness)
    }
    else {
        $body = ('{0} [{1}] ({2} {3}%)' -f (Get-PlanSwitchNotifyPrefix), $PlanName, (Get-BrightnessLabel), $Brightness)
    }
    return New-StatusNotificationEvent -Kind 'plan_switch' -Title $title -Body $body
}

function Get-PlanSwitchNotifyPrefix {
    return -join ([char]0x5DF2, [char]0x5207, [char]0x6362, [char]0x81F3)
}

function Get-BrightnessLabel {
    return -join ([char]0x4EAE, [char]0x5EA6)
}

function Format-ExternalPlanNotification {
    param([string]$PlanName, [string]$PlanGuid)
    $title = -join ([char]0x68C0, [char]0x6D4B, [char]0x5230, [char]0x5916, [char]0x90E8, [char]0x8BA1, [char]0x5212, [char]0x53D8, [char]0x66F4)
    $prefix = -join ([char]0x8BA1, [char]0x5212, [char]0x88AB, [char]0x5916, [char]0x90E8, [char]0x6539, [char]0x4E3A)
    $suffix = -join ([char]0xFF0C, [char]0x5B88, [char]0x62A4, [char]0x5C06, [char]0x5728, [char]0x4E0B, [char]0x8F6E, [char]0x8F6E, [char]0x8BE2, [char]0x65F6, [char]0x7EA0, [char]0x504F)
    $body = "$prefix [$PlanName]$suffix"
    return New-StatusNotificationEvent -Kind 'external_change' -Title $title -Body $body
}

function ConvertTo-StatusNotificationEvent {
    param($Raw)
    if (-not $Raw) { return $null }
    if ($Raw -is [hashtable]) { return $Raw }
    return @{
        id    = [string]$Raw.id
        kind  = [string]$Raw.kind
        title = [string]$Raw.title
        body  = [string]$Raw.body
        at    = [string]$Raw.at
    }
}

function Update-StatusNotificationRetention {
    param(
        $NewEvent,
        [datetime]$Now,
        [int]$RetainSeconds = 60
    )
    if ($NewEvent) {
        $script:RetainedNotification = $NewEvent
        $script:RetainedNotificationUntil = $Now.AddSeconds($RetainSeconds)
        return $NewEvent
    }
    if ($script:RetainedNotification -and $Now -le $script:RetainedNotificationUntil) {
        return $script:RetainedNotification
    }
    $script:RetainedNotification = $null
    $script:RetainedNotificationUntil = $null
    return $null
}
