# SmartPowerPlan.Tray.ps1 - single file tray + WPF settings
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

$scriptRoot = 'C:\Tools'
. (Join-Path $scriptRoot 'lib\SmartPowerPlan.Functions.ps1')
$configPath = Join-Path $scriptRoot 'SmartPowerPlan.config.json'
$statusPath = Join-Path $scriptRoot 'SmartPowerPlan.status.json'
$script:lastNotifiedPlan = $null

if (-not (Enter-SingleInstanceMutex -Name 'Tray')) {
    [System.Windows.Forms.MessageBox]::Show(
        '智能电源计划托盘已在运行。',
        '智能电源计划',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
    exit 0
}

function Get-SmartPowerPlanSettingsXamlText {
    $xamlPath = Join-Path $scriptRoot 'lib\SmartPowerPlan.Settings.xaml'
    if (-not (Test-Path $xamlPath)) {
        $writer = Join-Path $scriptRoot 'lib\Write-SmartPowerPlanSettingsXaml.ps1'
        if (Test-Path $writer) {
            & $writer -ScriptRoot $scriptRoot | Out-Null
        }
    }
    if (Test-Path $xamlPath) {
        $text = Read-TextFileAutoEncoding -Path $xamlPath
        if ($text -and $text.Trim().StartsWith('<')) { return $text }
    }
    throw '找不到设置界面文件 SmartPowerPlan.Settings.xaml'
}

function Initialize-SmartPowerPlanWpfApplication {
    if (-not ([System.Windows.Application]::Current)) {
        $null = New-Object System.Windows.Application
    }
}

function Get-TrayMenuFont {
    # DPI 感知启用后，SystemInformation.MenuFont 已含正确缩放，勿再乘 dpi/96
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

function Register-SettingsSliderLabel {
    param($Slider, $Label, [string]$Format)
    if ($null -eq $Slider) { throw 'Register-SettingsSliderLabel: Slider 为空' }
    if ($null -eq $Label) { throw 'Register-SettingsSliderLabel: Label 为空' }
    $sliderRef = $Slider
    $labelRef = $Label
    $formatRef = $Format
    $handler = {
        param($sender, $e)
        $labelRef.Text = $formatRef -f [int]$sender.Value
    }.GetNewClosure()
    $sliderRef.Add_ValueChanged($handler)
    $labelRef.Text = $formatRef -f ([int]$sliderRef.Value)
}

function Show-SmartPowerPlanSettings {
    param(
        [hashtable]$Config,
        [string]$ConfigPath,
        [scriptblock]$OnSaved
    )

    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
    Initialize-SmartPowerPlanWpfApplication

    try {
        $xaml = Get-SmartPowerPlanSettingsXamlText
        $window = [Windows.Markup.XamlReader]::Parse($xaml)
    }
    catch {
        $err = '设置界面加载失败：' + [Environment]::NewLine + $_.Exception.Message
        [System.Windows.MessageBox]::Show($err, '智能电源计划', 'OK', 'Error') | Out-Null
        return
    }

    $sldBalanced = $window.FindName('sldBalanced')
    $sldSaver = $window.FindName('sldSaver')
    $sldBattery = $window.FindName('sldBattery')
    $sldPoll = $window.FindName('sldPoll')
    $sldBrightMs = $window.FindName('sldBrightMs')
    $lblBalanced = $window.FindName('lblBalanced')
    $lblSaver = $window.FindName('lblSaver')
    $lblBattery = $window.FindName('lblBattery')
    $lblPoll = $window.FindName('lblPoll')
    $lblBrightMs = $window.FindName('lblBrightMs')
    $chkPaused = $window.FindName('chkPaused')
    $chkNotify = $window.FindName('chkNotify')
    $btnSave = $window.FindName('btnSave')
    $btnCancel = $window.FindName('btnCancel')

    $sldBalanced.Value = [math]::Max(1, [int]($Config.BalancedThresholdSec / 60))
    $sldSaver.Value = [math]::Max(2, [int]($Config.PowerSaverThresholdSec / 60))
    $sldBattery.Value = [int]$Config.LowBatteryPercent
    $sldPoll.Value = [int]$Config.CheckIntervalSec
    $sldBrightMs.Value = [int]$Config.BrightnessRestoreMs
    $chkPaused.IsChecked = [bool]$Config.Paused
    if ($null -ne $Config.NotifyOnPlanChange) {
        $chkNotify.IsChecked = [bool]$Config.NotifyOnPlanChange
    }
    else {
        $chkNotify.IsChecked = $true
    }

    Register-SettingsSliderLabel -Slider $sldBalanced -Label $lblBalanced -Format '{0} 分钟'
    Register-SettingsSliderLabel -Slider $sldSaver -Label $lblSaver -Format '{0} 分钟'
    Register-SettingsSliderLabel -Slider $sldBattery -Label $lblBattery -Format '{0}%'
    Register-SettingsSliderLabel -Slider $sldPoll -Label $lblPoll -Format '{0} 秒'
    Register-SettingsSliderLabel -Slider $sldBrightMs -Label $lblBrightMs -Format '{0} 毫秒'

    $cfgRef = $Config
    $pathRef = $ConfigPath
    $savedRef = $OnSaved
    $winRef = $window

    $btnCancel.Add_Click({
        $winRef.DialogResult = $false
        $winRef.Close()
    })

    $btnSave.Add_Click({
        $oldPaused = [bool]$cfgRef.Paused
        $newCfg = New-ConfigFromTraySettings -CurrentConfig $cfgRef -BalancedThresholdMin ([int]$sldBalanced.Value) -PowerSaverThresholdMin ([int]$sldSaver.Value) -LowBatteryPercent ([int]$sldBattery.Value) -CheckIntervalSec ([int]$sldPoll.Value) -BrightnessRestoreMs ([int]$sldBrightMs.Value) -Paused ([bool]$chkPaused.IsChecked) -NotifyOnPlanChange ([bool]$chkNotify.IsChecked)
        $errs = Test-SmartPowerPlanConfigValues -Config $newCfg
        if ($errs.Count -gt 0) {
            $msg = $errs -join [Environment]::NewLine
            [System.Windows.MessageBox]::Show($msg, '配置无效', 'OK', 'Warning') | Out-Null
            return
        }
        $pauseMsg = Get-PauseGuardLogMessage -PreviousPaused $oldPaused -CurrentPaused ([bool]$newCfg.Paused)
        if ($pauseMsg) { Write-SmartPowerPlanLog -Message $pauseMsg -Config $newCfg -FallbackLogPath (Get-SmartPowerPlanFallbackLogPath -ScriptRoot $scriptRoot) }
        Save-SmartPowerPlanConfig -Config $newCfg -ConfigPath $pathRef
        if ($null -ne $savedRef) {
            $savedRef.Invoke($newCfg)
        }
        $winRef.DialogResult = $true
        $winRef.Close()
    })

    $window.Topmost = $true
    try {
        $null = $window.ShowDialog()
    }
    finally {
        $window.Topmost = $false
    }
}

function Open-TraySettingsDeferred {
    if ($script:SettingsOpenTimer) { return }
    $script:SettingsOpenTimer = New-Object System.Windows.Forms.Timer
    $script:SettingsOpenTimer.Interval = 120
    $script:SettingsOpenTimer.Add_Tick({
        $script:SettingsOpenTimer.Stop()
        $script:SettingsOpenTimer.Dispose()
        $script:SettingsOpenTimer = $null
        try {
            $cfg = Read-SmartPowerPlanConfig -ConfigPath $script:TrayConfigPath
            if (-not $cfg) { $cfg = Get-DefaultSmartPowerPlanConfig }
            Show-SmartPowerPlanSettings -Config $cfg -ConfigPath $script:TrayConfigPath -OnSaved $script:TrayOnSettingsSaved
        }
        catch {
            [System.Windows.Forms.MessageBox]::Show(
                "打开设置失败：`n$($_.Exception.Message)",
                '智能电源计划',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error
            ) | Out-Null
        }
    })
    $script:SettingsOpenTimer.Start()
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

function Update-TrayDisplay {
    param($NotifyIcon, $StatusItem, [bool]$NotifyOnPlanChange = $true)
    $status = Read-SmartPowerPlanStatus -StatusPath $statusPath
    $NotifyIcon.Text = Format-TrayTooltip -Status $status
    if ($StatusItem) {
        if ($status) {
            $StatusItem.Text = Format-TrayStatusLine -Status $status
        } else { $StatusItem.Text = '等待核心服务…' }
    }
    if ($NotifyOnPlanChange -and $status -and (Test-PlanChangedForNotification -PreviousPlan $script:lastNotifiedPlan -CurrentPlan $status.currentPlan)) {
        $balloon = Format-PlanChangeBalloon -PlanName $status.currentPlan -Brightness $status.brightness
        $NotifyIcon.ShowBalloonTip(3000, '智能电源计划', $balloon, [System.Windows.Forms.ToolTipIcon]::Info)
    }
    if ($status -and $status.currentPlan) { $script:lastNotifiedPlan = $status.currentPlan }
}

$config = Read-SmartPowerPlanConfig -ConfigPath $configPath
if (-not $config) { $config = Get-DefaultSmartPowerPlanConfig }

$trayIcon = New-Object System.Windows.Forms.NotifyIcon
$trayIcon.Icon = Get-TrayNotifyIcon
$trayIcon.Visible = $true
$trayIcon.Text = '智能电源计划'

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
        $cfg = Read-SmartPowerPlanConfig -ConfigPath $configPath
        if (-not $cfg) { return }
        $cfg.Paused = -not $cfg.Paused
        Save-SmartPowerPlanConfig -Config $cfg -ConfigPath $configPath
        $pauseMsg = Get-PauseGuardLogMessage -PreviousPaused (-not $cfg.Paused) -CurrentPaused ([bool]$cfg.Paused)
        if ($pauseMsg) { Write-SmartPowerPlanLog -Message $pauseMsg -Config $cfg -FallbackLogPath (Get-SmartPowerPlanFallbackLogPath -ScriptRoot $scriptRoot) }
        $pauseItem.Text = if ($cfg.Paused) { '恢复守护' } else { '暂停守护' }
        $notify = if ($null -ne $cfg.NotifyOnPlanChange) { $cfg.NotifyOnPlanChange } else { $true }
        Update-TrayDisplay -NotifyIcon $trayIcon -StatusItem $statusItem -NotifyOnPlanChange $notify
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show(
            "操作失败：`n$($_.Exception.Message)",
            '智能电源计划',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
    }
})
$logItem.Add_Click({
    $cfg = Read-SmartPowerPlanConfig -ConfigPath $configPath
    $log = if ($cfg -and $cfg.LogFile) { $cfg.LogFile } else { Join-Path $scriptRoot 'SmartPowerPlan.log' }
    if (Test-Path $log) { Start-Process notepad.exe $log } else { [System.Windows.Forms.MessageBox]::Show('找不到日志文件', '智能电源计划') | Out-Null }
})
$openSettings = { Open-TraySettingsDeferred }
$settingsItem.Add_Click($openSettings)
$trayIcon.Add_DoubleClick($openSettings)
$exitItem.Add_Click({ $trayIcon.Visible = $false; $trayIcon.Dispose(); [System.Windows.Forms.Application]::Exit() })

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 5000
$timer.Add_Tick({
    $cfg = Read-SmartPowerPlanConfig -ConfigPath $configPath
    $notify = if ($cfg -and $null -ne $cfg.NotifyOnPlanChange) { $cfg.NotifyOnPlanChange } else { $true }
    Update-TrayDisplay -NotifyIcon $trayIcon -StatusItem $statusItem -NotifyOnPlanChange $notify
})
$timer.Start()

$pauseItem.Text = if ($config.Paused) { '恢复守护' } else { '暂停守护' }
$initNotify = if ($null -ne $config.NotifyOnPlanChange) { $config.NotifyOnPlanChange } else { $true }
Update-TrayDisplay -NotifyIcon $trayIcon -StatusItem $statusItem -NotifyOnPlanChange $initNotify
[System.Windows.Forms.Application]::Run()
