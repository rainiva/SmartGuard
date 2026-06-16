#Requires -RunAsAdministrator
$scriptRoot = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { 'C:\Tools' }
. (Join-Path $scriptRoot 'lib\SmartPowerPlan.Functions.ps1')
$configPath = Join-Path $scriptRoot 'SmartPowerPlan.config.json'
$statusPath = Join-Path $scriptRoot 'SmartPowerPlan.status.json'
$initMarkerPath = Join-Path $scriptRoot '.SmartPowerPlan.initialized'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class IdleTimeDetector {
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    public static uint GetIdleSeconds() {
        var lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        if (!GetLastInputInfo(ref lii)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return ((uint)Environment.TickCount - lii.dwTime) / 1000;
    }
}
"@ -Language CSharp

if (-not (Enter-SingleInstanceMutex -Name 'Core')) {
    $msg = @"
智能电源计划核心服务已在后台运行。

无需再次启动。
- 核心服务：计划任务「SmartPowerPlan Guardian」
- 托盘图标：请运行 Start-Tray.cmd
"@
    Write-Host $msg -ForegroundColor Yellow
    try {
        Add-Content -Path (Join-Path $scriptRoot 'SmartPowerPlan.startup.log') -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' 核心服务已在运行') -Encoding UTF8
        Add-Type -AssemblyName System.Windows.Forms
        [void][System.Windows.Forms.MessageBox]::Show($msg, '智能电源计划', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
    } catch {}
    exit 0
}

$script:Idempotency = New-GuardIdempotencyState

function Write-Log([string]$Message, [hashtable]$Config) {
    if (-not (Test-ShouldWriteLogMessage -State $script:Idempotency -Message $Message)) { return }
    Write-SmartPowerPlanLog -Message $Message -Config $Config -FallbackLogPath (Join-Path $scriptRoot 'SmartPowerPlan.startup.log')
    Register-WrittenLogFingerprint -State $script:Idempotency -Message $Message | Out-Null
}

function Get-CurrentPlanGuid { Get-CurrentPlanGuidFromOutput -PowerCfgListOutput (powercfg /getactivescheme 2>&1 | Out-String) }

function Get-CurrentBrightness {
    try { return [int](Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness -EA Stop | Select-Object -First 1 -ExpandProperty CurrentBrightness) }
    catch { return -1 }
}

function Set-Brightness([int]$Level, [hashtable]$Config) {
    if (-not (Test-ShouldApplyBrightnessLevel -TargetLevel $Level -CurrentLevel (Get-CurrentBrightness))) { return }
    if ($Level -lt 0 -or $Level -gt 100) { return }
    try {
        $m = Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods -EA Stop | Select-Object -First 1
        if ($m) { Invoke-CimMethod -InputObject $m -MethodName WmiSetBrightness -Arguments @{ Timeout = 1; Brightness = $Level } -EA Stop | Out-Null }
        Register-AppliedBrightnessLevel -State $script:Idempotency -Level $Level | Out-Null
    } catch { Write-Log "亮度设置失败: $($_.Exception.Message)" $Config }
}

function Get-BatteryInfo {
    try {
        $bat = Get-CimInstance Win32_Battery -EA Stop | Select-Object -First 1
        if (-not $bat) { return @{ Percent = 100; IsOnAC = $true } }
        return @{ Percent = [int]$bat.EstimatedChargeRemaining; IsOnAC = (@('2','3','6','7','8','9') -contains "$($bat.BatteryStatus)") }
    } catch { return @{ Percent = 100; IsOnAC = $true } }
}

function Initialize-SmartPowerPlan([hashtable]$Config) {
    if (Test-Path $initMarkerPath) { return }
    Write-Log 'INIT: 开始首次初始化...' $Config

    $supported = Initialize-PowerCfgSupport -Config $Config
    if ($supported) {
        foreach ($g in @($Config.ActivePlanGUID, $Config.BalancedPlanGUID, $Config.PowerSaverPlanGUID)) {
            Disable-AdaptiveBrightnessForPlan -PlanGuid $g | Out-Null
        }
        $b = Get-CurrentBrightness
        Sync-AllPlansBrightness -BrightnessLevel $b -Config $Config | Out-Null
        Write-Log "INIT: powercfg 三计划亮度对齐为 ${b}%" $Config
    }
    else {
        Write-Log 'INIT: 本机不支持 powercfg 亮度项，已切换为 WMI-only 模式（切计划后写回亮度）' $Config
    }

    Write-Log 'INIT: 请手动关闭 CABC 与节电模式降亮度' $Config
    New-Item -ItemType File -Path $initMarkerPath -Force | Out-Null
    Write-Log 'INIT: 首次初始化完成' $Config
}

function Publish-SmartPowerPlanStatus {
    param(
        [hashtable]$Payload,
        [hashtable]$NotificationEvent = $null
    )
    if ($NotificationEvent) {
        $Payload.notificationEvent = $NotificationEvent
    }
    Write-SmartPowerPlanStatusAtomic -Status $Payload -StatusPath $statusPath
}

if (-not (Test-Path $scriptRoot)) { New-Item -ItemType Directory -Path $scriptRoot -Force | Out-Null }
if (-not (Test-Path $configPath)) {
    Save-SmartPowerPlanConfig -Config (Get-DefaultSmartPowerPlanConfig) -ConfigPath $configPath
}
$config = Read-SmartPowerPlanConfig -ConfigPath $configPath
if (-not $config) {
    Write-Host '警告：config.json 无效，正在重建默认配置…'
    $config = Get-DefaultSmartPowerPlanConfig
    Save-SmartPowerPlanConfig -Config $config -ConfigPath $configPath
}
try {
    $config = Update-ConfigPlanGuidsFromSystem -Config $config
    Save-SmartPowerPlanConfig -Config $config -ConfigPath $configPath
    Initialize-SmartPowerPlan -Config $config
    Write-Host "智能电源计划核心服务运行中。日志：$($config.LogFile)"
}
catch {
    $err = "启动失败：$($_.Exception.Message)"
    Write-Host $err -ForegroundColor Red
    try {
        Add-Content -Path (Join-Path $scriptRoot 'SmartPowerPlan.startup.log') -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $err) -Encoding UTF8
    } catch {}
    Read-Host '按 Enter 键关闭此窗口'
    exit 1
}

$lastKnownGuid = $null; $lastStatusLabel = ''; $lastHeartbeat = (Get-Date); $scriptJustSwitched = $false
$pendingNotification = $null
while ($true) {
    $cur = $null
    Reset-IdempotencyTickLogs -State $script:Idempotency | Out-Null
    try {
        $config = Read-SmartPowerPlanConfig -ConfigPath $configPath
        if (-not $config) { $config = Get-DefaultSmartPowerPlanConfig }
        $idle = [IdleTimeDetector]::GetIdleSeconds()
        $bat = Get-BatteryInfo
        $cur = Get-CurrentPlanGuid
        $exp = Get-ExpectedPlanGuid -IdleSeconds $idle -IsOnAC $bat.IsOnAC -BatteryPercent $bat.Percent -Config $config
        $notifyEvent = $null

        if (Test-ExternalPlanChange -PreviousGuid $lastKnownGuid -CurrentGuid $cur -ScriptJustSwitched:$scriptJustSwitched) {
            Write-Log "EXTERNAL: 计划被外部改为 $(Get-PlanDisplayName $cur $config) ($cur) | 下轮纠偏" $config
            $notifyEvent = Format-ExternalPlanNotification -PlanName (Get-PlanDisplayName $cur $config) -PlanGuid $cur
        }
        $scriptJustSwitched = $false
        $label = if ($idle -ge $config.PowerSaverThresholdSec) { '深度空闲' } elseif ($idle -ge $config.BalancedThresholdSec) { '空闲' } else { '活跃' }
        $bright = Get-CurrentBrightness

        if ($exp -and (Test-ShouldApplyPowerPlanSwitch -CurrentGuid $cur -TargetGuid $exp)) {
            $res = Switch-PowerPlanWithBrightnessLock -TargetGuid $exp -BrightnessBefore $bright -Config $config -SetBrightnessInvoker { param([int]$L) Set-Brightness $L $config } -GetBrightnessInvoker { Get-CurrentBrightness }
            $scriptJustSwitched = $true
            $cur = Get-CurrentPlanGuid
            Register-AppliedPowerPlanSwitch -State $script:Idempotency -PlanGuid $cur | Out-Null
            $pwr = if ($bat.IsOnAC) { '插电' } else { '电池' }
            if ($bright -ge 0) {
                Write-Log "状态: $label | 计划切换(切前同步) + 亮度锁定: $($res.Before)% -> $($res.After)% | $(Get-PlanDisplayName $exp $config) | 电量$($bat.Percent)% $pwr" $config
                if ($res.After -ne $res.Before) {
                    Write-Log "WARN: 亮度写回未完全匹配，已重试 $($config.BrightnessRetryCount) 次" $config
                }
            }
            else { Write-Log "状态: $label | 计划切换(切前同步) | $(Get-PlanDisplayName $exp $config) | 亮度WMI不支持" $config }
            $lastStatusLabel = $label
            $notifyEvent = Format-PlanSwitchNotification -PlanName (Get-PlanDisplayName $exp $config) -Brightness $res.After -BrightnessBefore $res.Before
        } elseif ($lastStatusLabel -ne $label) {
            $pwr = if ($bat.IsOnAC) { '插电' } else { '电池' }
            Write-Log "状态: $label (空闲${idle}秒) | 计划正常 | $(Get-PlanDisplayName $cur $config) | 电量$($bat.Percent)% $pwr" $config
            $lastStatusLabel = $label
        }

        $heartbeatMin = if ($null -ne $config.HeartbeatIntervalMin) { [int]$config.HeartbeatIntervalMin } else { 10 }
        $now = Get-Date
        if (Test-HeartbeatDue -LastHeartbeat $lastHeartbeat -IntervalMinutes $heartbeatMin -Now $now) {
            $planName = if ($cur) { Get-PlanDisplayName $cur $config } else { '未知' }
            Write-Log (Format-HeartbeatLogMessage -Label $label -CurrentPlanName $planName -BatteryPercent $bat.Percent -IsOnAC $bat.IsOnAC -Paused ([bool]$config.Paused)) $config
            $lastHeartbeat = $now
        }

        $statusPayload = @{
            timestamp = (Get-Date).ToString('s')
            currentPlan = (Get-PlanDisplayName $cur $config)
            currentPlanGUID = $cur
            expectedPlan = $(if ($exp) { Get-PlanDisplayName $exp $config })
            idleSeconds = $idle
            isOnAC = $bat.IsOnAC
            batteryPercent = $bat.Percent
            brightness = $bright
            paused = [bool]$config.Paused
            lastExternalChange = $null
        }
        Publish-SmartPowerPlanStatus -Payload $statusPayload -NotificationEvent $notifyEvent
    } catch { Write-Log "ERROR: $($_.Exception.Message)" $config }
    $lastKnownGuid = $cur
    Start-Sleep -Seconds $config.CheckIntervalSec
}
