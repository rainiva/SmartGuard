# SmartPowerPlan.Functions.ps1

# powercfg 显示子组/设置：优先用别名（OEM 机器上 GUID 可能不一致）
$script:PowerCfgSubVideo = 'SUB_VIDEO'
$script:PowerCfgBrightness = 'VIDEONORMALLEVEL'
$script:PowerCfgAdaptiveAc = 'ADAPTBRIGHT'
$script:PowerCfgAdaptiveDc = 'ADAPTBRIGHT'

function Normalize-PlanGuid {
    param([string]$PlanGuid)
    if ($PlanGuid -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') {
        return $Matches[1].ToLower()
    }
    return $PlanGuid.Trim().ToLower()
}

function Get-ExpectedPlanGuid {
    param(
        [int]$IdleSeconds,
        [bool]$IsOnAC,
        [int]$BatteryPercent,
        [hashtable]$Config
    )

    if ($Config.Paused) { return $null }

    if ($IdleSeconds -ge $Config.PowerSaverThresholdSec) {
        return $Config.PowerSaverPlanGUID
    }
    if ($IdleSeconds -ge $Config.BalancedThresholdSec) {
        return $Config.BalancedPlanGUID
    }
    if ($IsOnAC -or $BatteryPercent -ge $Config.LowBatteryPercent) {
        return $Config.ActivePlanGUID
    }
    return $Config.BalancedPlanGUID
}

function Invoke-PowerCfgCommand {
    param([string[]]$Arguments, [scriptblock]$PowerCfgInvoker)
    if ($PowerCfgInvoker) {
        return & $PowerCfgInvoker (,$Arguments)
    }
    $outFile = [IO.Path]::GetTempFileName()
    $errFile = [IO.Path]::GetTempFileName()
    try {
        $null = Start-Process -FilePath 'powercfg.exe' -ArgumentList $Arguments -Wait -NoNewWindow -RedirectStandardOutput $outFile -RedirectStandardError $errFile
        $text = ((Get-Content $outFile -Raw -EA SilentlyContinue) + (Get-Content $errFile -Raw -EA SilentlyContinue))
        if ([string]::IsNullOrWhiteSpace($text)) {
            return @{ Output = ''; Success = $true }
        }
        return @{
            Output = $text
            Success = ($text -notmatch '不存在|无效|invalid|not exist|参数无效')
        }
    }
    catch {
        return @{ Output = $_.Exception.Message; Success = $false }
    }
    finally {
        Remove-Item $outFile, $errFile -EA SilentlyContinue
    }
}

function Get-InstalledPowerPlans {
    $r = Invoke-PowerCfgCommand -Arguments @('/list')
    $plans = @()
    foreach ($line in ($r.Output -split "`r?`n")) {
        if ($line -match 'GUID:\s*([0-9a-fA-F-]{36})\s*\(([^)]+)\)') {
            $plans += @{ Guid = $Matches[1].ToLower(); Name = $Matches[2].Trim() }
        }
    }
    return $plans
}

function Resolve-PlanGuidByName {
    param([array]$Plans, [string[]]$Keywords, [string]$Fallback)
    foreach ($kw in $Keywords) {
        $hit = $Plans | Where-Object { $_.Name -like "*$kw*" } | Select-Object -First 1
        if ($hit) { return $hit.Guid }
    }
    $fb = Normalize-PlanGuid -PlanGuid $Fallback
    if ($Plans.Guid -contains $fb) { return $fb }
    return $fb
}

function Update-ConfigPlanGuidsFromSystem {
    param([hashtable]$Config)
    $plans = Get-InstalledPowerPlans
    if (-not $plans -or $plans.Count -eq 0) { return $Config }
    $Config.ActivePlanGUID = Resolve-PlanGuidByName -Plans $plans -Keywords @('高性能','High performance','Ultimate','卓越') -Fallback $Config.ActivePlanGUID
    $Config.BalancedPlanGUID = Resolve-PlanGuidByName -Plans $plans -Keywords @('平衡','Balanced') -Fallback $Config.BalancedPlanGUID
    $Config.PowerSaverPlanGUID = Resolve-PlanGuidByName -Plans $plans -Keywords @('节能','Power saver') -Fallback $Config.PowerSaverPlanGUID
    return $Config
}

function Initialize-PowerCfgSupport {
    param([hashtable]$Config)
    $plan = Normalize-PlanGuid -PlanGuid $Config.BalancedPlanGUID
    $probe = Invoke-PowerCfgCommand -Arguments @('/setacvalueindex', $plan, $script:PowerCfgSubVideo, $script:PowerCfgBrightness, '50')
    $script:PowerCfgBrightnessSupported = [bool]$probe.Success
    return $script:PowerCfgBrightnessSupported
}

function Set-PlanVideoValue {
    param(
        [string]$PlanGuid,
        [ValidateSet('ac', 'dc')]
        [string]$AcOrDc,
        [string]$SettingId,
        [int]$Value,
        [scriptblock]$PowerCfgInvoker
    )
    if (-not $PowerCfgInvoker -and $script:PowerCfgBrightnessSupported -eq $false) { return $false }
    $scheme = Normalize-PlanGuid -PlanGuid $PlanGuid
    $cmd = if ($AcOrDc -eq 'ac') { '/setacvalueindex' } else { '/setdcvalueindex' }
    $r = Invoke-PowerCfgCommand -Arguments @($cmd, $scheme, $script:PowerCfgSubVideo, $SettingId, "$Value") -PowerCfgInvoker $PowerCfgInvoker
    if ($PowerCfgInvoker) { return $true }
    return [bool]$r.Success
}

function Sync-PlanBrightnessBeforeSwitch {
    param([string]$PlanGuid, [int]$BrightnessLevel, [scriptblock]$PowerCfgInvoker)
    if ($BrightnessLevel -lt 0 -or $BrightnessLevel -gt 100) { return }
    Set-PlanVideoValue -PlanGuid $PlanGuid -AcOrDc 'ac' -SettingId $script:PowerCfgBrightness -Value $BrightnessLevel -PowerCfgInvoker $PowerCfgInvoker
    Set-PlanVideoValue -PlanGuid $PlanGuid -AcOrDc 'dc' -SettingId $script:PowerCfgBrightness -Value $BrightnessLevel -PowerCfgInvoker $PowerCfgInvoker
}

function Sync-AllPlansBrightness {
    param([int]$BrightnessLevel, [hashtable]$Config, [scriptblock]$PowerCfgInvoker)
    if ($BrightnessLevel -lt 0) { return }
    if (-not $PowerCfgInvoker -and $script:PowerCfgBrightnessSupported -eq $false) { return }
    foreach ($guid in @($Config.ActivePlanGUID, $Config.BalancedPlanGUID, $Config.PowerSaverPlanGUID)) {
        Sync-PlanBrightnessBeforeSwitch -PlanGuid $guid -BrightnessLevel $BrightnessLevel -PowerCfgInvoker $PowerCfgInvoker
    }
}

function Disable-AdaptiveBrightnessForPlan {
    param([string]$PlanGuid, [scriptblock]$PowerCfgInvoker)
    if (-not $PowerCfgInvoker -and $script:PowerCfgBrightnessSupported -eq $false) { return }
    Set-PlanVideoValue -PlanGuid $PlanGuid -AcOrDc 'ac' -SettingId $script:PowerCfgAdaptiveAc -Value 0 -PowerCfgInvoker $PowerCfgInvoker
    Set-PlanVideoValue -PlanGuid $PlanGuid -AcOrDc 'dc' -SettingId $script:PowerCfgAdaptiveDc -Value 0 -PowerCfgInvoker $PowerCfgInvoker
}

function Switch-PowerPlanWithBrightnessLock {
    param(
        [string]$TargetGuid,
        [int]$BrightnessBefore,
        [hashtable]$Config,
        [scriptblock]$PowerCfgInvoker,
        [scriptblock]$SetBrightnessInvoker,
        [scriptblock]$GetBrightnessInvoker,
        [scriptblock]$SleepInvoker
    )

    if ($PowerCfgInvoker -or $script:PowerCfgBrightnessSupported -ne $false) {
        Sync-PlanBrightnessBeforeSwitch -PlanGuid $TargetGuid -BrightnessLevel $BrightnessBefore -PowerCfgInvoker $PowerCfgInvoker
    }
    Invoke-PowerCfgCommand -Arguments @('/setactive', (Normalize-PlanGuid $TargetGuid)) -PowerCfgInvoker $PowerCfgInvoker | Out-Null

    $sleepMs = if ($Config.BrightnessRestoreMs) { [int]$Config.BrightnessRestoreMs } else { 300 }
    if ($SleepInvoker) { & $SleepInvoker $sleepMs } else { Start-Sleep -Milliseconds $sleepMs }

    $brightnessAfter = $BrightnessBefore
    if ($BrightnessBefore -ge 0 -and $SetBrightnessInvoker -and $GetBrightnessInvoker) {
        $maxAttempts = if ($Config.BrightnessRetryCount) { [int]$Config.BrightnessRetryCount } else { 3 }
        $retryDelay = if ($Config.BrightnessRetryDelayMs) { [int]$Config.BrightnessRetryDelayMs } else { 100 }
        $retry = Restore-BrightnessWithRetry -TargetLevel $BrightnessBefore -MaxAttempts $maxAttempts -DelayMs $retryDelay -SetBrightness $SetBrightnessInvoker -GetBrightness $GetBrightnessInvoker -SleepInvoker $SleepInvoker
        $brightnessAfter = $retry.After
    }
    return @{ Before = $BrightnessBefore; After = $brightnessAfter }
}

function Restore-BrightnessWithRetry {
    param(
        [int]$TargetLevel,
        [int]$MaxAttempts = 3,
        [int]$DelayMs = 100,
        [scriptblock]$SetBrightness,
        [scriptblock]$GetBrightness,
        [scriptblock]$SleepInvoker
    )
    if ($TargetLevel -lt 0) { return @{ Before = $TargetLevel; After = $TargetLevel } }
    $after = $TargetLevel
    for ($i = 0; $i -lt $MaxAttempts; $i++) {
        & $SetBrightness $TargetLevel
        if ($SleepInvoker) { & $SleepInvoker $DelayMs } else { Start-Sleep -Milliseconds $DelayMs }
        $after = & $GetBrightness
        if ($after -eq $TargetLevel) { break }
    }
    return @{ Before = $TargetLevel; After = $after }
}

function Get-RotatedLogArchivePath {
    param([string]$LogPath)
    return "$LogPath.old"
}

function Get-SmartPowerPlanFallbackLogPath {
    param([string]$ScriptRoot = 'C:\Tools')
    return Join-Path $ScriptRoot 'SmartPowerPlan.startup.log'
}

function Add-SmartPowerPlanLogLine {
    param([string]$LogPath, [string]$Line)
    if ([string]::IsNullOrWhiteSpace($LogPath)) { return $false }
    $dir = Split-Path -Parent $LogPath
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $fs = [System.IO.File]::Open($LogPath, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
    try {
        $sw = New-Object System.IO.StreamWriter($fs, (New-Object System.Text.UTF8Encoding $false))
        $sw.WriteLine($Line)
        $sw.Flush()
        $sw.Dispose()
        return $true
    }
    finally {
        $fs.Dispose()
    }
}

function Invoke-LogRotationIfNeeded {
    param([string]$LogPath, [long]$MaxBytes = 1048576)
    if ([string]::IsNullOrWhiteSpace($LogPath)) { return }
    if (-not (Test-Path $LogPath)) { return }
    if ((Get-Item -LiteralPath $LogPath).Length -le $MaxBytes) { return }
    $archive = Get-RotatedLogArchivePath -LogPath $LogPath
    if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
    Move-Item -LiteralPath $LogPath -Destination $archive -Force
}

function Write-SmartPowerPlanLog {
    param(
        [string]$Message,
        [hashtable]$Config,
        [string]$FallbackLogPath = $null
    )
    if ([string]::IsNullOrEmpty($Config.LogFile)) { return $false }
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $Message"
    try {
        $maxBytes = if ($Config.LogMaxBytes) { [long]$Config.LogMaxBytes } else { 1048576 }
        Invoke-LogRotationIfNeeded -LogPath $Config.LogFile -MaxBytes $maxBytes
        if (Add-SmartPowerPlanLogLine -LogPath $Config.LogFile -Line $line) {
            return $true
        }
    }
    catch {}
    if (-not [string]::IsNullOrWhiteSpace($FallbackLogPath)) {
        try {
            $fallbackLine = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - [LOG-FALLBACK] $Message"
            if (Add-SmartPowerPlanLogLine -LogPath $FallbackLogPath -Line $fallbackLine) {
                return $false
            }
        }
        catch {}
    }
    return $false
}

function Format-HeartbeatLogMessage {
    param(
        [string]$Label,
        [string]$CurrentPlanName,
        [int]$BatteryPercent,
        [bool]$IsOnAC,
        [bool]$Paused
    )
    $pwr = if ($IsOnAC) { '插电' } else { '电池' }
    $pause = if ($Paused) { ' | 已暂停' } else { '' }
    return "[监控中] $Label | 计划正常 | $CurrentPlanName | 电量${BatteryPercent}% $pwr$pause"
}

function Test-HeartbeatDue {
    param(
        $LastHeartbeat,
        [int]$IntervalMinutes,
        [datetime]$Now
    )
    if ($IntervalMinutes -le 0) { return $false }
    if ($null -eq $LastHeartbeat) { return $false }
    return (($Now - $LastHeartbeat).TotalMinutes -ge $IntervalMinutes)
}

function Test-PlanChangedForNotification {
    param([string]$PreviousPlan, [string]$CurrentPlan)
    if ([string]::IsNullOrWhiteSpace($CurrentPlan)) { return $false }
    if ([string]::IsNullOrWhiteSpace($PreviousPlan)) { return $false }
    return ($PreviousPlan -ne $CurrentPlan)
}

function Format-PlanChangeBalloon {
    param([string]$PlanName, [int]$Brightness)
    return "已切换至 $PlanName（亮度 ${Brightness}%）"
}

function Get-SingleInstanceMutexName {
    param([string]$Component)
    return "Global\SmartPowerPlan.$Component"
}

function Enter-SingleInstanceMutex {
    param([string]$Name)
    try {
        $mutexName = if ($Name -match '^Global\\') { $Name } else { Get-SingleInstanceMutexName -Component $Name }
        $script:SmartPowerPlanInstanceMutex = New-Object System.Threading.Mutex($false, $mutexName)
        return $script:SmartPowerPlanInstanceMutex.WaitOne(0, $false)
    }
    catch { return $false }
}

function Get-TrayIconPath {
    param([string]$ScriptRoot = 'C:\Tools')
    return Join-Path $ScriptRoot 'lib\SmartPowerPlan.ico'
}

function Test-ExternalPlanChange {
    param([string]$PreviousGuid, [string]$CurrentGuid, [bool]$ScriptJustSwitched)
    if ($ScriptJustSwitched) { return $false }
    if ([string]::IsNullOrWhiteSpace($PreviousGuid)) { return $false }
    return ($PreviousGuid.ToLower() -ne $CurrentGuid.ToLower())
}

function Get-PlanDisplayName {
    param([string]$PlanGuid, [hashtable]$Config)
    $g = $PlanGuid.ToLower()
    switch ($g) {
        { $_ -eq $Config.ActivePlanGUID.ToLower() } { return '高性能' }
        { $_ -eq $Config.BalancedPlanGUID.ToLower() } { return '平衡' }
        { $_ -eq $Config.PowerSaverPlanGUID.ToLower() } { return '节能' }
        default { return $PlanGuid }
    }
}

function Get-CurrentPlanGuidFromOutput {
    param([string]$PowerCfgListOutput)
    if ($PowerCfgListOutput -match 'GUID:\s+([0-9a-fA-F-]{36})') { return $Matches[1].ToLower() }
    return $null
}

function Read-TextFileAutoEncoding {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        return [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    }
    $nullCount = ($bytes | Where-Object { $_ -eq 0 }).Count
    if ($nullCount -gt $bytes.Length / 4) {
        return [Text.Encoding]::Unicode.GetString($bytes)
    }
    return [Text.Encoding]::UTF8.GetString($bytes)
}

function Write-TextFileUtf8Bom {
    param([string]$Path, [string]$Content)
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [IO.File]::WriteAllText($Path, $Content, $utf8Bom)
}

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
    return @{
        ActivePlanGUID = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
        BalancedPlanGUID = '381b4222-f694-41f0-9685-ff5bb260df2e'
        PowerSaverPlanGUID = 'a1841308-3541-4fab-bc81-f71556f20b4a'
        BalancedThresholdSec = 300
        PowerSaverThresholdSec = 900
        LowBatteryPercent = 30
        CheckIntervalSec = 15
        BrightnessRestoreMs = 300
        LogFile = 'C:\Tools\SmartPowerPlan.log'
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

function Get-PauseGuardLogMessage {
    param(
        $PreviousPaused,
        [bool]$CurrentPaused
    )
    if ($null -eq $PreviousPaused) { return $null }
    if ([bool]$PreviousPaused -eq $CurrentPaused) { return $null }
    if ($CurrentPaused) { return '守护已暂停（仅监控，不切换计划）' }
    return '守护已恢复'
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

$_layersRoot = Join-Path $PSScriptRoot 'layers'
if (Test-Path $_layersRoot) {
    Get-ChildItem -LiteralPath $_layersRoot -Filter '*.ps1' -File |
        Where-Object { $_.Name -ne 'Import-Layers.ps1' } |
        Sort-Object Name |
        ForEach-Object { . $_.FullName }
}
