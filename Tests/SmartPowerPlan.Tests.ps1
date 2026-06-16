Describe 'SmartPowerPlan' {
    BeforeAll {
        $root = Split-Path -Parent $PSScriptRoot
        $functionsPath = Join-Path $root 'lib\SmartPowerPlan.Functions.ps1'
        $settingsPath = Join-Path $root 'lib\SmartPowerPlan.Settings.ps1'
        . $functionsPath
        . $settingsPath
        $script:PowerCfgBrightnessSupported = $true

        $script:TestConfig = @{
            ActivePlanGUID         = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
            BalancedPlanGUID       = '381b4222-f694-41f0-9685-ff5bb260df2e'
            PowerSaverPlanGUID     = 'a1841308-3541-4fab-bc81-f71556f20b4a'
            BalancedThresholdSec   = 300
            PowerSaverThresholdSec = 900
            LowBatteryPercent      = 30
            BrightnessRestoreMs    = 300
            Paused                 = $false
        }
    }

    Describe 'Get-ExpectedPlanGuid' {
        It 'returns null when paused' {
            $cfg = @{}
            foreach ($k in $script:TestConfig.Keys) { $cfg[$k] = $script:TestConfig[$k] }
            $cfg['Paused'] = $true
            Get-ExpectedPlanGuid -IdleSeconds 0 -IsOnAC $true -BatteryPercent 80 -Config $cfg | Should -BeNullOrEmpty
        }

        It 'returns power saver when idle >= 15 minutes' {
            $r = Get-ExpectedPlanGuid -IdleSeconds 900 -IsOnAC $true -BatteryPercent 80 -Config $script:TestConfig
            $r | Should -Be $script:TestConfig.PowerSaverPlanGUID
        }

        It 'returns balanced when idle between 5 and 15 minutes' {
            $r = Get-ExpectedPlanGuid -IdleSeconds 400 -IsOnAC $true -BatteryPercent 80 -Config $script:TestConfig
            $r | Should -Be $script:TestConfig.BalancedPlanGUID
        }

        It 'returns active plan when active on AC' {
            $r = Get-ExpectedPlanGuid -IdleSeconds 60 -IsOnAC $true -BatteryPercent 10 -Config $script:TestConfig
            $r | Should -Be $script:TestConfig.ActivePlanGUID
        }

        It 'returns active plan on battery when charge >= 30%' {
            $r = Get-ExpectedPlanGuid -IdleSeconds 60 -IsOnAC $false -BatteryPercent 50 -Config $script:TestConfig
            $r | Should -Be $script:TestConfig.ActivePlanGUID
        }

        It 'returns balanced on battery when charge < 30% and active' {
            $r = Get-ExpectedPlanGuid -IdleSeconds 60 -IsOnAC $false -BatteryPercent 25 -Config $script:TestConfig
            $r | Should -Be $script:TestConfig.BalancedPlanGUID
        }
    }

    Describe 'Sync-PlanBrightnessBeforeSwitch' {
        It 'writes AC and DC brightness to target plan via powercfg' {
            $calls = [System.Collections.Generic.List[string]]::new()
            $invoker = {
                param([string[]]$CommandArgs)
                $calls.Add(($CommandArgs -join ' ')) | Out-Null
                return 0
            }

            Sync-PlanBrightnessBeforeSwitch -PlanGuid '381b4222-f694-41f0-9685-ff5bb260df2e' -BrightnessLevel 75 -PowerCfgInvoker $invoker

            $calls.Count | Should -Be 2
            $calls[0] | Should -Match 'setacvalueindex 381b4222-f694-41f0-9685-ff5bb260df2e SUB_VIDEO VIDEONORMALLEVEL 75'
            $calls[1] | Should -Match 'setdcvalueindex 381b4222-f694-41f0-9685-ff5bb260df2e SUB_VIDEO VIDEONORMALLEVEL 75'
        }

        It 'skips powercfg when brightness is unsupported (-1)' {
            $calls = [System.Collections.Generic.List[string]]::new()
            $invoker = {
                param([string[]]$CommandArgs)
                $calls.Add('called') | Out-Null
            }

            Sync-PlanBrightnessBeforeSwitch -PlanGuid '381b4222-f694-41f0-9685-ff5bb260df2e' -BrightnessLevel -1 -PowerCfgInvoker $invoker

            $calls.Count | Should -Be 0
        }
    }

    Describe 'Switch-PowerPlanWithBrightnessLock' {
        It 'syncs powercfg brightness before setactive' {
            $calls = [System.Collections.Generic.List[string]]::new()
            $invoker = {
                param([string[]]$CommandArgs)
                $calls.Add(($CommandArgs -join ' ')) | Out-Null
                return 0
            }

            Switch-PowerPlanWithBrightnessLock `
                -TargetGuid $script:TestConfig.BalancedPlanGUID `
                -BrightnessBefore 75 `
                -Config $script:TestConfig `
                -PowerCfgInvoker $invoker `
                -SetBrightnessInvoker { param([int]$Level) } `
                -GetBrightnessInvoker { 75 } `
                -SleepInvoker { param([int]$Ms) }

            $preSyncAc = ($calls | Where-Object { $_ -match 'setacvalueindex' -and $_ -match 'VIDEONORMALLEVEL' } | Select-Object -First 1)
            $setActive = ($calls | Where-Object { $_ -match 'setactive' } | Select-Object -First 1)

            $preSyncAc | Should -Not -BeNullOrEmpty
            $setActive | Should -Not -BeNullOrEmpty
            ($calls.IndexOf($preSyncAc) -lt $calls.IndexOf($setActive)) | Should -BeTrue
        }
    }

    Describe 'Test-ExternalPlanChange' {
        It 'detects external plan change when GUID changes without script switch' {
            Test-ExternalPlanChange -PreviousGuid 'aaa' -CurrentGuid 'bbb' -ScriptJustSwitched $false | Should -Be $true
        }

        It 'ignores change when script just switched' {
            Test-ExternalPlanChange -PreviousGuid 'aaa' -CurrentGuid 'bbb' -ScriptJustSwitched $true | Should -Be $false
        }

        It 'returns false when GUID unchanged' {
            Test-ExternalPlanChange -PreviousGuid 'aaa' -CurrentGuid 'aaa' -ScriptJustSwitched $false | Should -Be $false
        }
    }

    Describe 'Phase2 Tray helpers' {
        It 'formats tray tooltip from status' {
            $tip = Format-TrayTooltip -Status @{
                currentPlan = '高性能'
                batteryPercent = 89
                isOnAC = $true
                brightness = 56
                paused = $false
            }
            $tip | Should -Match '高性能'
            $tip | Should -Match '89%'
            $tip | Should -Match '插电'
        }

        It 'shows paused marker in tooltip' {
            $tip = Format-TrayTooltip -Status @{ currentPlan = '平衡'; batteryPercent = 50; isOnAC = $false; brightness = 40; paused = $true }
            $tip | Should -Match '已暂停'
        }

        It 'returns pause log message when pause state changes' {
            Get-PauseGuardLogMessage -PreviousPaused $false -CurrentPaused $true | Should -Be '守护已暂停（仅监控，不切换计划）'
            Get-PauseGuardLogMessage -PreviousPaused $true -CurrentPaused $false | Should -Be '守护已恢复'
            Get-PauseGuardLogMessage -PreviousPaused $false -CurrentPaused $false | Should -BeNullOrEmpty
            Get-PauseGuardLogMessage -PreviousPaused $null -CurrentPaused $true | Should -BeNullOrEmpty
        }

        It 'formats tray menu status line with pause marker' {
            $line = Format-TrayStatusLine -Status @{
                currentPlan = '高性能'
                batteryPercent = 89
                isOnAC = $true
                paused = $true
            }
            $line | Should -Match '高性能'
            $line | Should -Match '已暂停'
        }

        It 'validates config thresholds' {
            $bad = Test-SmartPowerPlanConfigValues -Config @{
                BalancedThresholdSec = 300
                PowerSaverThresholdSec = 200
                LowBatteryPercent = 30
                CheckIntervalSec = 15
                BrightnessRestoreMs = 300
            }
            $bad.Count | Should -BeGreaterThan 0
        }

        It 'builds config from tray minute-based settings' {
            $cfg = New-ConfigFromTraySettings -CurrentConfig $script:TestConfig -BalancedThresholdMin 5 -PowerSaverThresholdMin 15 -LowBatteryPercent 30 -CheckIntervalSec 15 -BrightnessRestoreMs 300 -Paused $false
            $cfg.BalancedThresholdSec | Should -Be 300
            $cfg.PowerSaverThresholdSec | Should -Be 900
        }
    }

    Describe 'Phase3 enhancements' {
        It 'retries brightness restore until readback matches' {
            $script:readCalls = 0
            $script:setCount = 0
            $get = {
                $script:readCalls++
                if ($script:readCalls -lt 3) { return 48 }
                return 56
            }
            $set = { param([int]$Level) $script:setCount++ }
            $r = Restore-BrightnessWithRetry -TargetLevel 56 -MaxAttempts 5 -DelayMs 1 -SetBrightness $set -GetBrightness $get -SleepInvoker { param([int]$Ms) }
            $r.After | Should -Be 56
            $script:setCount | Should -BeGreaterOrEqual 3
        }

        It 'rotates log when file exceeds max bytes' {
            $tmp = Join-Path $TestDrive 'rotate.log'
            ('x' * 200) | Out-File -FilePath $tmp -Encoding UTF8 -NoNewline
            Invoke-LogRotationIfNeeded -LogPath $tmp -MaxBytes 100
            Test-Path $tmp | Should -Be $false
            Test-Path (Get-RotatedLogArchivePath -LogPath $tmp) | Should -Be $true
        }

        It 'leaves small log unchanged' {
            $tmp = Join-Path $TestDrive 'small.log'
            'hi' | Out-File -FilePath $tmp -Encoding UTF8
            Invoke-LogRotationIfNeeded -LogPath $tmp -MaxBytes 1000
            Test-Path $tmp | Should -Be $true
        }

        It 'appends log lines to file' {
            $tmp = Join-Path $TestDrive 'append.log'
            $cfg = @{ LogFile = $tmp; LogMaxBytes = 1048576 }
            Write-SmartPowerPlanLog -Message 'line one' -Config $cfg
            Write-SmartPowerPlanLog -Message 'line two' -Config $cfg
            $text = Get-Content -LiteralPath $tmp -Raw
            $text | Should -Match 'line one'
            $text | Should -Match 'line two'
        }

        It 'returns true when primary log write succeeds' {
            $tmp = Join-Path $TestDrive 'ok.log'
            $cfg = @{ LogFile = $tmp; LogMaxBytes = 1048576 }
            Write-SmartPowerPlanLog -Message 'ok' -Config $cfg | Should -Be $true
        }

        It 'falls back to startup log when primary path is not writable' {
            $blocked = Join-Path $TestDrive 'blocked.log'
            New-Item -ItemType Directory -Path $blocked -Force | Out-Null
            $fallback = Join-Path $TestDrive 'fallback.log'
            $cfg = @{ LogFile = $blocked; LogMaxBytes = 1048576 }
            Write-SmartPowerPlanLog -Message 'hello' -Config $cfg -FallbackLogPath $fallback | Should -Be $false
            $text = Get-Content -LiteralPath $fallback -Raw
            $text | Should -Match 'LOG-FALLBACK'
            $text | Should -Match 'hello'
        }

        It 'formats heartbeat log message' {
            $msg = Format-HeartbeatLogMessage -Label '活跃' -CurrentPlanName '高性能' -BatteryPercent 89 -IsOnAC $true -Paused $true
            $msg | Should -Match '^\[监控中\]'
            $msg | Should -Match '已暂停'
            $msg | Should -Match '高性能'
        }

        It 'detects heartbeat interval elapsed' {
            $now = Get-Date '2026-06-15T12:00:00'
            Test-HeartbeatDue -LastHeartbeat $null -IntervalMinutes 30 -Now $now | Should -Be $false
            Test-HeartbeatDue -LastHeartbeat $now.AddMinutes(-31) -IntervalMinutes 30 -Now $now | Should -Be $true
            Test-HeartbeatDue -LastHeartbeat $now.AddMinutes(-5) -IntervalMinutes 30 -Now $now | Should -Be $false
            Test-HeartbeatDue -LastHeartbeat $null -IntervalMinutes 0 -Now $now | Should -Be $false
        }

        It 'detects plan change for tray notification' {
            Test-PlanChangedForNotification -PreviousPlan '平衡' -CurrentPlan '节能' | Should -Be $true
            Test-PlanChangedForNotification -PreviousPlan '平衡' -CurrentPlan '平衡' | Should -Be $false
            Test-PlanChangedForNotification -PreviousPlan '' -CurrentPlan '平衡' | Should -Be $false
        }

        It 'formats plan change balloon text' {
            $t = Format-PlanChangeBalloon -PlanName '节能' -Brightness 56
            $t | Should -Match '节能'
            $t | Should -Match '56%'
        }

        It 'returns stable single-instance mutex name' {
            Get-SingleInstanceMutexName -Component 'Core' | Should -Be 'Global\SmartPowerPlan.Core'
            Get-SingleInstanceMutexName -Component 'Tray' | Should -Be 'Global\SmartPowerPlan.Tray'
        }
    }

    Describe 'Tray assets' {
        It 'resolves tray icon path' {
            $root = Split-Path -Parent $PSScriptRoot
            Get-TrayIconPath -ScriptRoot $root | Should -Match 'SmartPowerPlan\.ico$'
        }

        It 'Create-TrayIcon.ps1 generates icon file in clean process' {
            $root = Split-Path -Parent $PSScriptRoot
            $script = Join-Path $root 'lib\Create-TrayIcon.ps1'
            $icon = Join-Path $root 'lib\SmartPowerPlan.ico'
            if (Test-Path $icon) { Remove-Item $icon -Force }
            $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script 2>&1 | Out-String
            $output | Should -Not -Match 'Get-TrayIconPath'
            Test-Path $icon | Should -Be $true
        }
    }

    Describe 'WPF Settings UI' {
        It 'resolves external xaml path' {
            Get-SmartPowerPlanSettingsXamlPath | Should -Match 'SmartPowerPlan\.Settings\.xaml$'
        }

        It 'writes and loads settings xaml' {
            $root = Split-Path -Parent $PSScriptRoot
            $writer = Join-Path $root 'lib\Write-SmartPowerPlanSettingsXaml.ps1'
            $xamlPath = Get-SmartPowerPlanSettingsXamlPath -ScriptRoot $root
            if (Test-Path $writer) {
                & $writer -ScriptRoot $root | Out-Null
            }
            Test-Path $xamlPath | Should -Be $true
            $text = Resolve-SmartPowerPlanSettingsXaml -ScriptRoot $root
            $text | Should -Match '<Window'
            $text | Should -Match 'x:Name="sldBalanced"'
        }

        It 'keeps slider label binding after Register-SettingsSliderLabel returns' {
            Add-Type -AssemblyName PresentationFramework
            if (-not ([System.Windows.Application]::Current)) {
                $null = New-Object System.Windows.Application
            }
            $tb = New-Object System.Windows.Controls.TextBlock
            $s = New-Object System.Windows.Controls.Slider
            $s.Minimum = 1
            $s.Maximum = 10
            $s.Value = 3
            Register-SettingsSliderLabel -Slider $s -Label $tb -Format '{0} 分钟'
            $s.Value = 7
            $tb.Text | Should -Be '7 分钟'
        }
    }
}
