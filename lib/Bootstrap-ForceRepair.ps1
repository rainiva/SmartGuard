$ErrorActionPreference = 'Stop'
$root = 'C:\Tools'
$lib = Join-Path $root 'lib'

function Write-BomFile {
    param([string]$Path, [string]$Content)
    $parent = Split-Path $Path -Parent
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $enc = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
}

function Repair-FileEncoding {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254) {
        $text = [System.Text.Encoding]::Unicode.GetString($bytes)
        Write-BomFile -Path $Path -Content $text
        Write-Host ('Repaired UTF-16 BOM: ' + $Path)
        return $true
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
        return $false
    }
    $nulls = 0
    foreach ($b in $bytes) {
        if ($b -eq 0) { $nulls++ }
    }
    if ($nulls -gt ($bytes.Length / 4)) {
        $text = [System.Text.Encoding]::Unicode.GetString($bytes)
        Write-BomFile -Path $Path -Content $text
        Write-Host ('Repaired UTF-16: ' + $Path)
        return $true
    }
    return $false
}

$settingsContent = @'
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
            . $writer -ScriptRoot $ScriptRoot
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
        $newCfg = New-ConfigFromTraySettings -CurrentConfig $cfgRef -BalancedThresholdMin ([int]$sldBalanced.Value) -PowerSaverThresholdMin ([int]$sldSaver.Value) -LowBatteryPercent ([int]$sldBattery.Value) -CheckIntervalSec ([int]$sldPoll.Value) -BrightnessRestoreMs ([int]$sldBrightMs.Value) -Paused ([bool]$chkPaused.IsChecked) -NotifyOnPlanChange ([bool]$chkNotify.IsChecked)
        $errs = Test-SmartPowerPlanConfigValues -Config $newCfg
        if ($errs.Count -gt 0) {
            $msg = $errs -join [Environment]::NewLine
            [System.Windows.MessageBox]::Show($msg, '配置无效', 'OK', 'Warning') | Out-Null
            return
        }
        Save-SmartPowerPlanConfig -Config $newCfg -ConfigPath $pathRef
        if ($null -ne $savedRef) {
            $savedRef.Invoke($newCfg)
        }
        $winRef.DialogResult = $true
        $winRef.Close()
    })

    $null = $window.ShowDialog()
}
'@

Write-BomFile -Path (Join-Path $lib 'SmartPowerPlan.Settings.ps1') -Content $settingsContent
Write-Host ('Forced write: ' + (Join-Path $lib 'SmartPowerPlan.Settings.ps1'))

$items = Get-ChildItem -Path $lib -Filter '*.ps1' -File
foreach ($item in $items) {
    Repair-FileEncoding -Path $item.FullName | Out-Null
}

$writer = Join-Path $lib 'Write-SmartPowerPlanSettingsXaml.ps1'
Repair-FileEncoding -Path $writer | Out-Null
. $writer -ScriptRoot $root
Write-Host 'Bootstrap complete.'
