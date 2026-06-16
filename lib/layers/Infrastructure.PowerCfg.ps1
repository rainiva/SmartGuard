# Infrastructure: powercfg 命令封装

$script:PowerCfgSubVideo = 'SUB_VIDEO'
$script:PowerCfgBrightness = 'VIDEONORMALLEVEL'
$script:PowerCfgAdaptiveAc = 'ADAPTBRIGHT'
$script:PowerCfgAdaptiveDc = 'ADAPTBRIGHT'

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
