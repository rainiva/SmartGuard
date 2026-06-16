# Infrastructure: 配置与状态持久化

function Read-SmartPowerPlanConfig {
    param([string]$ConfigPath)
    if (-not (Test-Path $ConfigPath)) { return $null }
    try {
        $text = (Read-TextFileAutoEncoding -Path $ConfigPath) -replace "`0", ''
        $raw = $text | ConvertFrom-Json
        return @{
            ActivePlanGUID = (Normalize-PlanGuid -PlanGuid ([string]$raw.ActivePlanGUID))
            BalancedPlanGUID = (Normalize-PlanGuid -PlanGuid ([string]$raw.BalancedPlanGUID))
            PowerSaverPlanGUID = (Normalize-PlanGuid -PlanGuid ([string]$raw.PowerSaverPlanGUID))
            BalancedThresholdSec = [int]$raw.BalancedThresholdSec
            PowerSaverThresholdSec = [int]$raw.PowerSaverThresholdSec
            LowBatteryPercent = [int]$raw.LowBatteryPercent
            CheckIntervalSec = [int]$raw.CheckIntervalSec
            BrightnessRestoreMs = [int]$raw.BrightnessRestoreMs
            LogFile = [string]$raw.LogFile
            Paused = [bool]$raw.Paused
            LogMaxBytes = if ($null -ne $raw.LogMaxBytes) { [long]$raw.LogMaxBytes } else { 1048576 }
            BrightnessRetryCount = if ($null -ne $raw.BrightnessRetryCount) { [int]$raw.BrightnessRetryCount } else { 3 }
            BrightnessRetryDelayMs = if ($null -ne $raw.BrightnessRetryDelayMs) { [int]$raw.BrightnessRetryDelayMs } else { 100 }
            NotifyOnPlanChange = if ($null -ne $raw.NotifyOnPlanChange) { [bool]$raw.NotifyOnPlanChange } else { $true }
            HeartbeatIntervalMin = if ($null -ne $raw.HeartbeatIntervalMin) { [int]$raw.HeartbeatIntervalMin } else { 10 }
            AutoStartEnabled = if ($null -ne $raw.AutoStartEnabled) { [bool]$raw.AutoStartEnabled } else { $true }
        }
    }
    catch {
        return $null
    }
}

function Save-SmartPowerPlanConfig {
    param([hashtable]$Config, [string]$ConfigPath)
    $obj = [ordered]@{
        ActivePlanGUID = $Config.ActivePlanGUID
        BalancedPlanGUID = $Config.BalancedPlanGUID
        PowerSaverPlanGUID = $Config.PowerSaverPlanGUID
        BalancedThresholdSec = $Config.BalancedThresholdSec
        PowerSaverThresholdSec = $Config.PowerSaverThresholdSec
        LowBatteryPercent = $Config.LowBatteryPercent
        CheckIntervalSec = $Config.CheckIntervalSec
        BrightnessRestoreMs = $Config.BrightnessRestoreMs
        LogFile = $Config.LogFile
        Paused = $Config.Paused
        LogMaxBytes = if ($Config.LogMaxBytes) { [long]$Config.LogMaxBytes } else { 1048576 }
        BrightnessRetryCount = if ($Config.BrightnessRetryCount) { [int]$Config.BrightnessRetryCount } else { 3 }
        BrightnessRetryDelayMs = if ($Config.BrightnessRetryDelayMs) { [int]$Config.BrightnessRetryDelayMs } else { 100 }
        NotifyOnPlanChange = if ($null -ne $Config.NotifyOnPlanChange) { [bool]$Config.NotifyOnPlanChange } else { $true }
        HeartbeatIntervalMin = if ($null -ne $Config.HeartbeatIntervalMin) { [int]$Config.HeartbeatIntervalMin } else { 10 }
        AutoStartEnabled = if ($null -ne $Config.AutoStartEnabled) { [bool]$Config.AutoStartEnabled } else { $true }
    }
    Write-TextFileUtf8Bom -Path $ConfigPath -Content ($obj | ConvertTo-Json -Depth 3)
}

function Get-DefaultSmartPowerPlanConfig {
    param([string]$ScriptRoot = $null)
    $root = Get-SmartPowerPlanRoot -ScriptRoot $ScriptRoot
    return @{
        ActivePlanGUID = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
        BalancedPlanGUID = '381b4222-f694-41f0-9685-ff5bb260df2e'
        PowerSaverPlanGUID = 'a1841308-3541-4fab-bc81-f71556f20b4a'
        BalancedThresholdSec = 300
        PowerSaverThresholdSec = 900
        LowBatteryPercent = 30
        CheckIntervalSec = 15
        BrightnessRestoreMs = 300
        LogFile = Join-Path $root 'SmartPowerPlan.log'
        Paused = $false
        LogMaxBytes = 1048576
        BrightnessRetryCount = 3
        BrightnessRetryDelayMs = 100
        NotifyOnPlanChange = $true
        HeartbeatIntervalMin = 10
        AutoStartEnabled = $true
    }
}

function Read-SmartPowerPlanStatus {
    param([string]$StatusPath)
    if (-not (Test-Path $StatusPath)) { return $null }
    try {
        $text = (Read-TextFileAutoEncoding -Path $StatusPath) -replace "`0", ''
        $raw = $text | ConvertFrom-Json
        return @{
            timestamp = [string]$raw.timestamp
            currentPlan = [string]$raw.currentPlan
            currentPlanGUID = [string]$raw.currentPlanGUID
            expectedPlan = [string]$raw.expectedPlan
            idleSeconds = [int]$raw.idleSeconds
            isOnAC = [bool]$raw.isOnAC
            batteryPercent = [int]$raw.batteryPercent
            brightness = [int]$raw.brightness
            paused = [bool]$raw.paused
            lastExternalChange = $raw.lastExternalChange
            notificationEvent = ConvertTo-StatusNotificationEvent -Raw $raw.notificationEvent
        }
    }
    catch { return $null }
}

function Format-TrayTooltip {
    param([hashtable]$Status)
    if (-not $Status) { return '智能电源计划（等待核心服务）' }
    $power = if ($Status.isOnAC) { '插电' } else { '电池' }
    $pause = if ($Status.paused) { ' [已暂停]' } else { '' }
    return "计划: $($Status.currentPlan) | $($Status.batteryPercent)% $power | 亮度$($Status.brightness)%$pause"
}

function Format-TrayStatusLine {
    param([hashtable]$Status)
    if (-not $Status) { return '等待核心服务…' }
    $power = if ($Status.isOnAC) { '插电' } else { '电池' }
    $pause = if ($Status.paused) { ' | 已暂停' } else { '' }
    return "计划：$($Status.currentPlan) | $($Status.batteryPercent)% $power$pause"
}

function Test-SmartPowerPlanConfigValues {
    param([hashtable]$Config)
    $errors = @()
    if ($Config.BalancedThresholdSec -lt 60) { $errors += '平衡阈值至少 60 秒' }
    if ($Config.PowerSaverThresholdSec -le $Config.BalancedThresholdSec) { $errors += '节能阈值必须大于平衡阈值' }
    if ($Config.LowBatteryPercent -lt 0 -or $Config.LowBatteryPercent -gt 100) { $errors += '低电量百分比须在 0~100' }
    if ($Config.CheckIntervalSec -lt 5) { $errors += '轮询间隔至少 5 秒' }
    if ($Config.BrightnessRestoreMs -lt 0) { $errors += '亮度恢复延迟不能为负' }
    return $errors
}

function New-ConfigFromTraySettings {
    param(
        [hashtable]$CurrentConfig,
        [int]$BalancedThresholdMin,
        [int]$PowerSaverThresholdMin,
        [int]$LowBatteryPercent,
        [int]$CheckIntervalSec,
        [int]$BrightnessRestoreMs,
        [bool]$Paused,
        [bool]$NotifyOnPlanChange = $true,
        [bool]$AutoStartEnabled = $true
    )
    $cfg = @{}
    foreach ($k in $CurrentConfig.Keys) { $cfg[$k] = $CurrentConfig[$k] }
    $cfg.BalancedThresholdSec = $BalancedThresholdMin * 60
    $cfg.PowerSaverThresholdSec = $PowerSaverThresholdMin * 60
    $cfg.LowBatteryPercent = $LowBatteryPercent
    $cfg.CheckIntervalSec = $CheckIntervalSec
    $cfg.BrightnessRestoreMs = $BrightnessRestoreMs
    $cfg.Paused = $Paused
    $cfg.NotifyOnPlanChange = $NotifyOnPlanChange
    $cfg.AutoStartEnabled = $AutoStartEnabled
    return $cfg
}
