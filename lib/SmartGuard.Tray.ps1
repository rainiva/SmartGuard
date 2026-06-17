# SmartGuard.Tray.ps1 - 表现层：托盘 UI（设置/日志见 Settings.ps1 与 layers）
#Requires -Version 5.1
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function Enable-TrayProcessDpiAwareness {
    try {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TrayDpiAware {
    [DllImport("user32.dll", SetLastError=true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    private static readonly IntPtr PerMonitorV2 = new IntPtr(-4);
    public static void Enable() { SetProcessDpiAwarenessContext(PerMonitorV2); }
}
"@ -ErrorAction Stop
        [TrayDpiAware]::Enable()
    }
    catch {
        try {
            Add-Type @"
using System.Runtime.InteropServices;
public static class TrayDpiLegacy { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@ -ErrorAction Stop
            [void][TrayDpiLegacy]::SetProcessDPIAware()
        }
        catch {}
    }
}

Enable-TrayProcessDpiAwareness

$scriptRoot = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { 'D:\Project\SmartGuard' }
. (Join-Path $scriptRoot 'lib\SmartGuard.Functions.ps1')
. (Join-Path $scriptRoot 'lib\SmartGuard.Settings.ps1')
$configPath = Join-Path $scriptRoot 'SmartGuard.config.json'
$statusPath = Join-Path $scriptRoot 'SmartGuard.status.json'
$script:lastNotifiedEventId = $null

if (-not (Enter-SingleInstanceMutex -Name 'Tray')) {
    [System.Windows.Forms.MessageBox]::Show(
        '智能电源守护托盘已在运行。',
        '智能电源守护',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
    exit 0
}

Initialize-SmartGuardToastRegistration -ScriptRoot $scriptRoot | Out-Null

function Get-TrayMenuFont {
    return [System.Windows.Forms.SystemInformation]::MenuFont
}

function Apply-TrayMenuStyle {
    param([System.Windows.Forms.ContextMenuStrip]$Menu)
    try {
        $font = Get-TrayMenuFont
        $Menu.Font = $font
        $Menu.ShowImageMargin = $false
        $Menu.AutoSize = $true
        foreach ($item in $Menu.Items) {
            if ($item -is [System.Windows.Forms.ToolStripMenuItem]) {
                $item.Font = $font
                $item.AutoSize = $true
            }
        }
    }
    catch {}
}

function Invoke-TrayStatusNotification {
    param(
        [hashtable]$Status,
        [bool]$NotifyOnPlanChange = $true,
        $NotifyIcon = $null
    )
    if (-not $NotifyOnPlanChange -or -not $Status) { return }
    $event = $Status.notificationEvent
    if (-not $event -or -not $event.id) {
        if ($Status.currentPlan -and (Test-PlanChangedForNotification -PreviousPlan $script:lastLegacyPlan -CurrentPlan $Status.currentPlan)) {
            $balloon = Format-PlanChangeBalloon -PlanName $Status.currentPlan -Brightness $Status.brightness
            if ($NotifyIcon) {
                $NotifyIcon.ShowBalloonTip(5000, '智能电源守护', $balloon, [System.Windows.Forms.ToolTipIcon]::Warning)
            }
            $script:lastLegacyPlan = $Status.currentPlan
        }
        return
    }
    if (-not (Test-ShouldShowStatusNotification -LastEventId $script:lastNotifiedEventId -Event $event)) { return }
    $title = if ($event.title) { $event.title } else { '智能电源守护' }
    $body = if ($event.body) { $event.body } else { $Status.currentPlan }
    $shown = Show-SmartGuardToast -Title $title -Body $body -Tag $event.id -ScriptRoot $scriptRoot
    if (-not $shown -and $NotifyIcon) {
        $NotifyIcon.ShowBalloonTip(5000, $title, $body, [System.Windows.Forms.ToolTipIcon]::Warning)
    }
    $script:lastNotifiedEventId = $event.id
}

function Update-TrayDisplay {
    param($NotifyIcon, $StatusItem, [bool]$NotifyOnPlanChange = $true)
    $status = Read-SmartGuardStatus -StatusPath $statusPath
    $NotifyIcon.Text = Format-TrayTooltip -Status $status
    if ($StatusItem) {
        if ($status) {
            $StatusItem.Text = Format-TrayStatusLine -Status $status
        } else { $StatusItem.Text = '等待核心服务…' }
    }
    Invoke-TrayStatusNotification -Status $status -NotifyOnPlanChange $NotifyOnPlanChange -NotifyIcon $NotifyIcon
}

function Open-TraySettingsDeferred {
    if ($script:SettingsOpenTimer) {
        try { $script:SettingsOpenTimer.Stop(); $script:SettingsOpenTimer.Dispose() } catch {}
        $script:SettingsOpenTimer = $null
    }
    $script:SettingsOpenTimer = New-Object System.Windows.Forms.Timer
    $script:SettingsOpenTimer.Interval = 120
    $script:SettingsOpenTimer.Add_Tick({
        param($sender, $e)
        $sender.Stop()
        $sender.Dispose()
        $script:SettingsOpenTimer = $null
        try {
            $cfg = Read-SmartGuardConfig -ConfigPath $script:TrayConfigPath
            if (-not $cfg) { $cfg = Get-DefaultSmartGuardConfig }
            Show-SmartGuardSettings -Config $cfg -ConfigPath $script:TrayConfigPath -ScriptRoot $scriptRoot -OnSaved $script:TrayOnSettingsSaved
        }
        catch {
            [System.Windows.Forms.MessageBox]::Show(
                "打开设置失败：`n$($_.Exception.Message)",
                '智能电源守护',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error
            ) | Out-Null
        }
    })
    $script:SettingsOpenTimer.Start()
}

function Open-TrayLogViewer {
    try {
        Start-SmartGuardLogViewerProcess -ScriptRoot $scriptRoot
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show(
            "打开日志失败：`n$($_.Exception.Message)",
            '智能电源守护',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
    }
}

function Get-TrayNotifyIcon {
    $iconPath = Get-TrayIconPath -ScriptRoot $scriptRoot
    if (-not (Test-Path $iconPath)) {
        $iconScript = Join-Path $scriptRoot 'lib\Create-TrayIcon.ps1'
        if (Test-Path $iconScript) {
            . $iconScript | Out-Null
        }
    }
    if (Test-Path $iconPath) {
        try { return New-Object System.Drawing.Icon $iconPath }
        catch {}
    }
    return [System.Drawing.SystemIcons]::Shield
}

$config = Read-SmartGuardConfig -ConfigPath $configPath
if (-not $config) { $config = Get-DefaultSmartGuardConfig }

$trayIcon = New-Object System.Windows.Forms.NotifyIcon
$trayIcon.Icon = Get-TrayNotifyIcon
$trayIcon.Visible = $true
$trayIcon.Text = '智能电源守护'

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$statusItem = $menu.Items.Add('加载中…')
$statusItem.Enabled = $false
[void]$menu.Items.Add('-')
$pauseItem = $menu.Items.Add('暂停守护')
$logItem = $menu.Items.Add('打开日志')
$settingsItem = $menu.Items.Add('设置…')
[void]$menu.Items.Add('-')
$exitItem = $menu.Items.Add('退出')
Apply-TrayMenuStyle -Menu $menu
$trayIcon.ContextMenuStrip = $menu

$script:TrayConfigPath = $configPath
$script:TrayOnSettingsSaved = {
    param($newCfg)
    $pauseItem.Text = if ($newCfg.Paused) { '恢复守护' } else { '暂停守护' }
    $notify = if ($null -ne $newCfg.NotifyOnPlanChange) { $newCfg.NotifyOnPlanChange } else { $true }
    Update-TrayDisplay -NotifyIcon $trayIcon -StatusItem $statusItem -NotifyOnPlanChange $notify
}

$pauseItem.Add_Click({
    try {
        $cfg = Read-SmartGuardConfig -ConfigPath $configPath
        if (-not $cfg) { return }
        $cfg.Paused = -not $cfg.Paused
        Save-SmartGuardConfig -Config $cfg -ConfigPath $configPath
        $pauseMsg = Get-PauseGuardLogMessage -PreviousPaused (-not $cfg.Paused) -CurrentPaused ([bool]$cfg.Paused)
        if ($pauseMsg) { Write-SmartGuardLog -Message $pauseMsg -Config $cfg -FallbackLogPath (Get-SmartGuardFallbackLogPath -ScriptRoot $scriptRoot) }
        $pauseItem.Text = if ($cfg.Paused) { '恢复守护' } else { '暂停守护' }
        $notify = if ($null -ne $cfg.NotifyOnPlanChange) { $cfg.NotifyOnPlanChange } else { $true }
        Update-TrayDisplay -NotifyIcon $trayIcon -StatusItem $statusItem -NotifyOnPlanChange $notify
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show(
            "操作失败：`n$($_.Exception.Message)",
            '智能电源守护',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
    }
})

$logItem.Add_Click({ Open-TrayLogViewer })

$openSettings = { Open-TraySettingsDeferred }
$settingsItem.Add_Click($openSettings)
$trayIcon.Add_DoubleClick($openSettings)
$exitItem.Add_Click({ $trayIcon.Visible = $false; $trayIcon.Dispose(); [System.Windows.Forms.Application]::Exit() })

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1500
$timer.Add_Tick({
    $cfg = Read-SmartGuardConfig -ConfigPath $configPath
    $notify = if ($cfg -and $null -ne $cfg.NotifyOnPlanChange) { $cfg.NotifyOnPlanChange } else { $true }
    Update-TrayDisplay -NotifyIcon $trayIcon -StatusItem $statusItem -NotifyOnPlanChange $notify
})
$timer.Start()

$script:lastLegacyPlan = $null
$pauseItem.Text = if ($config.Paused) { '恢复守护' } else { '暂停守护' }
$initNotify = if ($null -ne $config.NotifyOnPlanChange) { $config.NotifyOnPlanChange } else { $true }
Update-TrayDisplay -NotifyIcon $trayIcon -StatusItem $statusItem -NotifyOnPlanChange $initNotify
[System.Windows.Forms.Application]::Run()
