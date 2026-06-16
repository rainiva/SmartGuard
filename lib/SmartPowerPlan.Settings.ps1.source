#Requires -Version 5.1
function Get-SmartPowerPlanSettingsXamlPath {
    param([string]$ScriptRoot = 'C:\Tools')
    return Join-Path $ScriptRoot 'lib\SmartPowerPlan.Settings.xaml'
}

function Initialize-SmartPowerPlanWpfApplication {
    if (-not ([System.Windows.Application]::Current)) {
        $null = New-Object System.Windows.Application
    }
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

function Resolve-SmartPowerPlanSettingsXaml {
    param([string]$ScriptRoot = 'C:\Tools')
    $xamlPath = Get-SmartPowerPlanSettingsXamlPath -ScriptRoot $ScriptRoot
    if (-not (Test-Path $xamlPath)) {
        $writer = Join-Path $ScriptRoot 'lib\Write-SmartPowerPlanSettingsXaml.ps1'
        if (Test-Path $writer) {
            & $writer -ScriptRoot $ScriptRoot
        }
    }
    if (-not (Test-Path $xamlPath)) {
        throw ('Missing XAML: ' + $xamlPath)
    }
    $text = Read-TextFileAutoEncoding -Path $xamlPath
    if (-not $text -or -not $text.Trim().StartsWith('<')) {
        throw ('XAML 无效：' + $xamlPath)
    }
    return $text
}

function Show-SmartPowerPlanSettings {
    param(
        [hashtable]$Config,
        [string]$ConfigPath,
        [string]$ScriptRoot = 'C:\Tools',
        [scriptblock]$OnSaved
    )

    if (-not (Get-Command Read-SmartPowerPlanConfig -ErrorAction SilentlyContinue)) {
        . (Join-Path $ScriptRoot 'lib\SmartPowerPlan.Functions.ps1')
    }

    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
    Initialize-SmartPowerPlanWpfApplication

    try {
        $xaml = Resolve-SmartPowerPlanSettingsXaml -ScriptRoot $ScriptRoot
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
    $tglPaused = $window.FindName('tglPaused')
    $tglNotify = $window.FindName('tglNotify')
    $tglAutoStart = $window.FindName('tglAutoStart')
    $btnSave = $window.FindName('btnSave')
    $btnCancel = $window.FindName('btnCancel')

    $sldBalanced.Value = [math]::Max(1, [int]($Config.BalancedThresholdSec / 60))
    $sldSaver.Value = [math]::Max(2, [int]($Config.PowerSaverThresholdSec / 60))
    $sldBattery.Value = [int]$Config.LowBatteryPercent
    $sldPoll.Value = [int]$Config.CheckIntervalSec
    $sldBrightMs.Value = [int]$Config.BrightnessRestoreMs
    $tglPaused.IsChecked = [bool]$Config.Paused
    if ($null -ne $Config.NotifyOnPlanChange) {
        $tglNotify.IsChecked = [bool]$Config.NotifyOnPlanChange
    }
    else {
        $tglNotify.IsChecked = $true
    }
    if ($null -ne $Config.AutoStartEnabled) {
        $tglAutoStart.IsChecked = [bool]$Config.AutoStartEnabled
    }
    else {
        $tglAutoStart.IsChecked = (Get-SmartPowerPlanAutoStartEnabled)
    }

    Register-SettingsSliderLabel -Slider $sldBalanced -Label $lblBalanced -Format '{0} 分钟'
    Register-SettingsSliderLabel -Slider $sldSaver -Label $lblSaver -Format '{0} 分钟'
    Register-SettingsSliderLabel -Slider $sldBattery -Label $lblBattery -Format '{0}%'
    Register-SettingsSliderLabel -Slider $sldPoll -Label $lblPoll -Format '{0} 秒'
    Register-SettingsSliderLabel -Slider $sldBrightMs -Label $lblBrightMs -Format '{0} 毫秒'

    $cfgRef = $Config
    $pathRef = $ConfigPath
    $rootRef = $ScriptRoot
    $savedRef = $OnSaved
    $winRef = $window

    $btnCancel.Add_Click({
        $winRef.DialogResult = $false
        $winRef.Close()
    })

    $btnSave.Add_Click({
        $oldPaused = [bool]$cfgRef.Paused
        $newCfg = New-ConfigFromTraySettings -CurrentConfig $cfgRef -BalancedThresholdMin ([int]$sldBalanced.Value) -PowerSaverThresholdMin ([int]$sldSaver.Value) -LowBatteryPercent ([int]$sldBattery.Value) -CheckIntervalSec ([int]$sldPoll.Value) -BrightnessRestoreMs ([int]$sldBrightMs.Value) -Paused ([bool]$tglPaused.IsChecked) -NotifyOnPlanChange ([bool]$tglNotify.IsChecked) -AutoStartEnabled ([bool]$tglAutoStart.IsChecked)
        $errs = Test-SmartPowerPlanConfigValues -Config $newCfg
        if ($errs.Count -gt 0) {
            $msg = $errs -join [Environment]::NewLine
            [System.Windows.MessageBox]::Show($msg, '配置无效', 'OK', 'Warning') | Out-Null
            return
        }
        $pauseMsg = Get-PauseGuardLogMessage -PreviousPaused $oldPaused -CurrentPaused ([bool]$newCfg.Paused)
        if ($pauseMsg) {
            Write-SmartPowerPlanLog -Message $pauseMsg -Config $newCfg -FallbackLogPath (Get-SmartPowerPlanFallbackLogPath -ScriptRoot $rootRef)
        }
        Save-SmartPowerPlanConfig -Config $newCfg -ConfigPath $pathRef
        Set-SmartPowerPlanAutoStart -Enabled ([bool]$newCfg.AutoStartEnabled) -ScriptRoot $rootRef
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
