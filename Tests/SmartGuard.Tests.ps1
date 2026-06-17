Describe 'SmartGuard' {
    BeforeAll {
        $root = Split-Path -Parent $PSScriptRoot
        $functionsPath = Join-Path $root 'lib\SmartGuard.Functions.ps1'
        $settingsPath = Join-Path $root 'lib\SmartGuard.Settings.ps1'
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

    Describe 'Get-PlanDisplayName' {
        It 'prefers config alias over system catalog' {
            $cfg = $script:TestConfig.Clone()
            $catalog = @{ $cfg.BalancedPlanGUID.ToLower() = '平衡(系统名)' }
            Get-PlanDisplayName -PlanGuid $cfg.BalancedPlanGUID -Config $cfg -Catalog $catalog | Should -Be '平衡'
        }

        It 'uses system catalog for OEM plan names' {
            $cfg = $script:TestConfig.Clone()
            $oem = 'b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f'
            $catalog = @{ $oem.ToLower() = 'Honor Performance' }
            Get-PlanDisplayName -PlanGuid $oem -Config $cfg -Catalog $catalog | Should -Be 'Honor Performance'
        }

        It 'uses preferred name when catalog misses' {
            $cfg = $script:TestConfig.Clone()
            $oem = 'b8a2c9f4-7d3e-4a1b-9c2f-5e8d6a3b1c4f'
            Get-PlanDisplayName -PlanGuid $oem -Config $cfg -Catalog @{} -PreferredName 'Honor Performance' | Should -Be 'Honor Performance'
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
            $bad = Test-SmartGuardConfigValues -Config @{
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
            Write-SmartGuardLog -Message 'line one' -Config $cfg
            Write-SmartGuardLog -Message 'line two' -Config $cfg
            $text = Get-Content -LiteralPath $tmp -Raw
            $text | Should -Match 'line one'
            $text | Should -Match 'line two'
        }

        It 'writes level tag before timestamp' {
            $line = Format-SmartGuardLogLine -Message 'EXTERNAL: plan changed' -Timestamp (Get-Date '2026-06-16T16:48:46')
            $line | Should -Be '[WARN] 2026-06-16 16:48:46 EXTERNAL: plan changed'
        }

        It 'returns true when primary log write succeeds' {
            $tmp = Join-Path $TestDrive 'ok.log'
            $cfg = @{ LogFile = $tmp; LogMaxBytes = 1048576 }
            Write-SmartGuardLog -Message 'ok' -Config $cfg | Should -Be $true
        }

        It 'falls back to startup log when primary path is not writable' {
            $blocked = Join-Path $TestDrive 'blocked.log'
            New-Item -ItemType Directory -Path $blocked -Force | Out-Null
            $fallback = Join-Path $TestDrive 'fallback.log'
            $cfg = @{ LogFile = $blocked; LogMaxBytes = 1048576 }
            Write-SmartGuardLog -Message 'hello' -Config $cfg -FallbackLogPath $fallback | Should -Be $false
            $text = Get-Content -LiteralPath $fallback -Raw
            $text | Should -Match 'LOG-FALLBACK'
            $text | Should -Match 'hello'
        }

        It 'formats heartbeat log message' {
            $msg = Format-HeartbeatLogMessage -Label '活跃' -CurrentPlanName '高性能' -BatteryPercent 89 -IsOnAC $true -Paused $true -Brightness 56
            $msg | Should -Match '^\[监控中\]'
            $msg | Should -Match '已暂停'
            $msg | Should -Match '高性能'
            $msg | Should -Match '亮度56%'
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
            Get-SingleInstanceMutexName -Component 'Core' | Should -Be 'Global\SmartGuard.Core'
            Get-SingleInstanceMutexName -Component 'Tray' | Should -Be 'Global\SmartGuard.Tray'
        }
    }

    Describe 'Tray assets' {
        It 'resolves tray icon path' {
            $root = Split-Path -Parent $PSScriptRoot
            Get-TrayIconPath -ScriptRoot $root | Should -Match 'SmartGuard\.ico$'
        }

        It 'bundled SmartGuard.ico exists for tray and installers' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'lib\SmartGuard.ico'
            Test-Path -LiteralPath $icon | Should -Be $true
            (Get-Item -LiteralPath $icon).Length | Should -BeGreaterThan 1000
        }

        It 'Create-TrayIcon.ps1 verifies bundled icon without regenerating' {
            $root = Split-Path -Parent $PSScriptRoot
            $script = Join-Path $root 'lib\Create-TrayIcon.ps1'
            $icon = Join-Path $root 'lib\SmartGuard.ico'
            Test-Path -LiteralPath $icon | Should -Be $true
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script | Out-Null
            $LASTEXITCODE | Should -Be 0
        }
    }

    Describe 'WPF Settings UI' {
        It 'resolves external xaml path' {
            Get-SmartGuardSettingsXamlPath | Should -Match 'SmartGuard\.Settings\.xaml$'
        }

        It 'writes and loads settings xaml' {
            $root = Split-Path -Parent $PSScriptRoot
            $writer = Join-Path $root 'lib\Write-SmartGuardSettingsXaml.ps1'
            $xamlPath = Get-SmartGuardSettingsXamlPath -ScriptRoot $root
            if (Test-Path $writer) {
                & $writer -ScriptRoot $root | Out-Null
            }
            Test-Path $xamlPath | Should -Be $true
            $text = Resolve-SmartGuardSettingsXaml -ScriptRoot $root
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

        It 'includes toggle switches and autostart in generated xaml' {
            $root = Split-Path -Parent $PSScriptRoot
            $writer = Join-Path $root 'lib\Write-SmartGuardSettingsXaml.ps1'
            & $writer -ScriptRoot $root | Out-Null
            $text = Resolve-SmartGuardSettingsXaml -ScriptRoot $root
            $text | Should -Match 'x:Name="tglPaused"'
            $text | Should -Match 'x:Name="tglAutoStart"'
            $text | Should -Match 'ToggleSwitch'
        }

        It 'parses settings xaml without template setter errors' {
            Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
            if (-not ([System.Windows.Application]::Current)) {
                $null = New-Object System.Windows.Application
            }
            $root = Split-Path -Parent $PSScriptRoot
            & (Join-Path $root 'lib\Write-SmartGuardSettingsXaml.ps1') -ScriptRoot $root | Out-Null
            $xaml = Resolve-SmartGuardSettingsXaml -ScriptRoot $root
            { [void][Windows.Markup.XamlReader]::Parse($xaml) } | Should -Not -Throw
        }

        It 'reads log file content for live viewer merge' {
            $logPath = Join-Path $TestDrive 'viewer-primary.log'
            $fallbackPath = Join-Path $TestDrive 'viewer-fallback.log'
            Set-Content -LiteralPath $logPath -Value '[INFO] 2026-06-16 17:00:00 main log line' -Encoding UTF8
            Set-Content -LiteralPath $fallbackPath -Value '2026-06-16 16:00:00 startup log line' -Encoding UTF8
            $merged = Read-SmartGuardLogText -LogPath $logPath -FallbackLogPath $fallbackPath
            $merged | Should -Not -BeNullOrEmpty
            $merged.IndexOf('16:00:00') | Should -BeLessThan ($merged.IndexOf('17:00:00'))
            $merged | Should -Not -Match '--- fallback ---'
        }

        It 'reads appended log bytes without reloading entire file' {
            $temp = Join-Path $env:TEMP ("spp-log-{0}.txt" -f [guid]::NewGuid().ToString('N'))
            try {
                [System.IO.File]::WriteAllText($temp, "line-1$([Environment]::NewLine)", [System.Text.UTF8Encoding]::new($false))
                $first = Read-LogFileTextFromOffset -Path $temp -StartOffset 0
                [System.IO.File]::AppendAllText($temp, "line-2$([Environment]::NewLine)", [System.Text.UTF8Encoding]::new($false))
                $delta = Read-LogFileTextFromOffset -Path $temp -StartOffset $first.Length
                $delta.Text | Should -Match 'line-2'
                $delta.Text | Should -Not -Match 'line-1'
            }
            finally {
                Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
            }
        }

        It 'reuses a single WPF Application when opening settings repeatedly' {
            Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
            Initialize-SmartGuardWpfApplication | Out-Null
            { Initialize-SmartGuardWpfApplication | Out-Null } | Should -Not -Throw
            $app = if ($script:SmartGuardWpfApplication) {
                $script:SmartGuardWpfApplication
            }
            else {
                [System.Windows.Application]::Current
            }
            $app | Should -Not -BeNullOrEmpty
            $app.ShutdownMode | Should -Be ([System.Windows.ShutdownMode]::OnExplicitShutdown)
        }

        It 'loads log text into viewer form before show' {
            Add-Type -AssemblyName System.Windows.Forms, System.Drawing
            $root = Split-Path -Parent $PSScriptRoot
            $logPath = Join-Path $root 'SmartGuard.log'
            if (-not (Test-Path -LiteralPath $logPath)) {
                Set-Content -LiteralPath $logPath -Value '2026-01-01 00:00:00 - test log line' -Encoding UTF8
            }
            $form = New-SmartGuardLogViewerForm -LogPath $logPath
            Initialize-SmartGuardLogViewerSession -Form $form
            $form.Tag.RichTextBox.Text | Should -Not -BeNullOrEmpty
            $form.Tag.StatusLabel.Text | Should -Match '行'
            $form.Tag.RefreshTimer.Stop()
            $form.Dispose()
        }

        It 'starts log viewer without visible console window when falling back to ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'lib\layers\Presentation.LogViewer.ps1') -Raw -Encoding UTF8
            $content | Should -Match 'Start-Process.*WindowStyle Hidden'
            $content | Should -Match '-WindowStyle.*Hidden'
        }

        It 'Start-SmartGuardLogViewerProcess prefers SmartGuard.LogViewer.exe with PS fallback' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'lib\layers\Presentation.LogViewer.ps1') -Raw -Encoding UTF8
            $match = [regex]::Match($content, '(?s)function Start-SmartGuardLogViewerProcess\s*\{.*?\r?\n\}')
            $match.Success | Should -Be $true
            $fn = $match.Value
            $fn | Should -Match 'SmartGuard\.LogViewer\.exe'
            $fn | Should -Match 'Get-SmartGuardLogViewerScriptPath'
            $fn | Should -Match 'powershell\.exe'
            $fn.IndexOf('SmartGuard.LogViewer.exe') | Should -BeLessThan $fn.IndexOf('Get-SmartGuardLogViewerScriptPath')
        }

        It 'exposes log viewer launcher and standalone script' {
            Get-Command Start-SmartGuardLogViewerProcess -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
            $root = Split-Path -Parent $PSScriptRoot
            Get-SmartGuardLogViewerScriptPath -ScriptRoot $root | Should -Be (Join-Path $root 'lib\Show-LogViewer.ps1')
            Test-Path (Get-SmartGuardLogViewerScriptPath -ScriptRoot $root) | Should -Be $true
        }

        It 'skips autostart task update when toggle unchanged' {
            Test-SmartGuardAutoStartNeedsUpdate -Enabled $true -PreviousEnabled $true | Should -Be $false
            Test-SmartGuardAutoStartNeedsUpdate -Enabled $false -PreviousEnabled $false | Should -Be $false
            Test-SmartGuardAutoStartNeedsUpdate -Enabled $true -PreviousEnabled $false | Should -Be $true
            Test-SmartGuardAutoStartNeedsUpdate -Enabled $true -PreviousEnabled $null | Should -Be $true
        }
    }

    Describe 'Layer modules' {
        It 'defaults heartbeat interval to 10 minutes' {
            $cfg = Get-DefaultSmartGuardConfig
            $cfg.HeartbeatIntervalMin | Should -Be 10
        }

        It 'skips duplicate power plan switch when already active' {
            Test-ShouldApplyPowerPlanSwitch -CurrentGuid 'aaa' -TargetGuid 'aaa' | Should -Be $false
            Test-ShouldApplyPowerPlanSwitch -CurrentGuid 'aaa' -TargetGuid 'bbb' | Should -Be $true
        }

        It 'dedupes log messages within same tick' {
            $state = New-GuardIdempotencyState
            Test-ShouldWriteLogMessage -State $state -Message 'same' | Should -Be $true
            Register-WrittenLogFingerprint -State $state -Message 'same' | Out-Null
            Test-ShouldWriteLogMessage -State $state -Message 'same' | Should -Be $false
        }

        It 'creates status notification events with unique ids' {
            $a = Format-PlanSwitchNotification -PlanName '高性能' -Brightness 56
            $b = Format-PlanSwitchNotification -PlanName '平衡' -Brightness 40
            $a.id | Should -Not -Be $b.id
            $a.title | Should -Match '切换'
        }

        It 'shows notification only once per event id' {
            Test-ShouldShowStatusNotification -LastEventId 'abc' -Event @{ id = 'abc' } | Should -Be $false
            Test-ShouldShowStatusNotification -LastEventId 'abc' -Event @{ id = 'xyz' } | Should -Be $true
        }

        It 'retains status notification until expiry' {
            $script:RetainedNotification = $null
            $script:RetainedNotificationUntil = $null
            $now = Get-Date '2026-06-16T12:00:00'
            $evt = Format-PlanSwitchNotification -PlanName '高性能' -Brightness 56
            $active = Update-StatusNotificationRetention -NewEvent $evt -Now $now
            $active.id | Should -Be $evt.id
            $later = Update-StatusNotificationRetention -NewEvent $null -Now $now.AddSeconds(30)
            $later.id | Should -Be $evt.id
            $expired = Update-StatusNotificationRetention -NewEvent $null -Now $now.AddSeconds(61)
            $expired | Should -BeNullOrEmpty
        }

        It 'resolves battery percent from system API before WMI' {
            $resolved = Resolve-SmartGuardBatteryInfo -AcLineStatus 0 -BatteryLifePercent 55 -BatteryFlag 0 -WmiPercent 90 -WmiOnAc $true
            $resolved.Percent | Should -Be 55
            $resolved.IsOnAC | Should -Be $false
        }

        It 'does not treat unknown WMI battery status as AC' {
            ConvertFrom-SmartGuardWmiBatteryStatus -BatteryStatus 2 | Should -Be $null
            ConvertFrom-SmartGuardWmiBatteryStatus -BatteryStatus 3 | Should -Be $null
        }

        It 'aggregates multiple WMI batteries by design capacity' {
            $batteries = @(
                [pscustomobject]@{ EstimatedChargeRemaining = 80; DesignCapacity = 50000 }
                [pscustomobject]@{ EstimatedChargeRemaining = 40; DesignCapacity = 50000 }
            )
            Get-SmartGuardAggregatedBatteryPercent -Batteries $batteries | Should -Be 60
        }
    }

    Describe 'Engine packaging' {
        It 'builds core engine without console window' {
            $root = Split-Path -Parent $PSScriptRoot
            $csproj = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Engine\SmartGuard.Engine.csproj') -Raw
            $csproj | Should -Match '<OutputType>WinExe</OutputType>'
        }
    }

    Describe 'Phase 3.1 install CLI' {
        It 'Program routes install and uninstall commands' {
            $root = Split-Path -Parent $PSScriptRoot
            $program = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Engine\Program.cs') -Raw -Encoding UTF8
            $program | Should -Match 'EngineCommandMode\.Install'
            $program | Should -Match 'EngineCommandMode\.Uninstall'
            $program | Should -Match 'InstallCommands\.RunInstall'
            $program | Should -Match 'InstallCommands\.RunUninstall'
        }
    }

    Describe 'Phase 6.1 scheduled task registrar' {
        It 'InstallCommands registers tasks via ScheduledTaskRegistrar not PowerShell scripts' {
            $root = Split-Path -Parent $PSScriptRoot
            $install = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Engine\Cli\InstallCommands.cs') -Raw -Encoding UTF8
            $install | Should -Match 'ScheduledTaskRegistrar\.RegisterGuardian'
            $install | Should -Match 'ScheduledTaskRegistrar\.RegisterTray'
            $install | Should -Not -Match 'RunPowerShellScript'
            $install | Should -Not -Match 'Register-SmartGuardTask\.ps1'
        }

        It 'AutoStartService registers missing tasks via ScheduledTaskRegistrar' {
            $root = Split-Path -Parent $PSScriptRoot
            $autoStart = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\AutoStartService.cs') -Raw -Encoding UTF8
            $autoStart | Should -Match 'ScheduledTaskRegistrar\.RegisterIfMissing'
            $autoStart | Should -Not -Match 'Register-SmartGuardTask\.ps1'
            $autoStart | Should -Not -Match 'Register-TrayTask\.ps1'
        }
    }

    Describe 'Phase 1 launcher and tasks' {
        It 'Start-Core prefers C# engine with PS fallback' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Start-Core.ps1') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Engine\.exe'
            $content | Should -Match 'SmartGuard\.Core\.ps1'
            $content.IndexOf('SmartGuard.Engine.exe') | Should -BeLessThan $content.IndexOf('SmartGuard.Core.ps1')
        }

        It 'Register-TrayTask resolves paths from script root' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Register-TrayTask.ps1') -Raw -Encoding UTF8
            $content | Should -Match '\$PSScriptRoot'
            $content | Should -Not -Match 'D:\\Project\\SmartGuard'
        }

        It 'Register-TrayTask prefers SmartGuard.Tray.exe with PS fallback' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Register-TrayTask.ps1') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Tray\.exe'
            $content | Should -Match 'SmartGuard\.Tray\.ps1'
            $content.IndexOf('SmartGuard.Tray.exe') | Should -BeLessThan $content.IndexOf('SmartGuard.Tray.ps1')
        }

        It 'Tray OpenLogViewer prefers SmartGuard.LogViewer.exe with PS fallback' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Tray\Infrastructure.cs') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.LogViewer\.exe'
            $content | Should -Match 'Show-LogViewer\.ps1'
            $content.IndexOf('SmartGuard.LogViewer.exe') | Should -BeLessThan $content.IndexOf('Show-LogViewer.ps1')
        }

        It 'Tray OpenSettings prefers SmartGuard.Settings.exe with PS fallback' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Tray\Infrastructure.cs') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Settings\.exe'
            $content | Should -Match 'SmartGuard\.Settings\.ps1'
            $content.IndexOf('SmartGuard.Settings.exe') | Should -BeLessThan $content.IndexOf('SmartGuard.Settings.ps1')
        }

        It 'Register-SmartGuardTask resolves paths from script root' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Register-SmartGuardTask.ps1') -Raw -Encoding UTF8
            $content | Should -Match '\$PSScriptRoot'
            $content | Should -Match 'SmartGuard\.Engine\.exe'
            $content | Should -Match '--root'
        }
    }

    Describe 'Bootstrap and maintenance' {
        It 'does not embed legacy chkPaused settings overwrite' {
            $root = Split-Path -Parent $PSScriptRoot
            $bootstrap = Join-Path $root 'lib\Bootstrap-ForceRepair.ps1'
            $content = Get-Content -LiteralPath $bootstrap -Raw -Encoding UTF8
            $content | Should -Not -Match "FindName\('chkPaused'\)"
        }

        It 'keeps settings source aligned with toggle controls' {
            $root = Split-Path -Parent $PSScriptRoot
            $source = Join-Path $root 'lib\SmartGuard.Settings.ps1.source'
            Test-Path $source | Should -Be $true
            $content = Get-Content -LiteralPath $source -Raw -Encoding UTF8
            $content | Should -Match 'tglPaused'
            $content | Should -Match 'tglAutoStart'
            $content | Should -Not -Match "FindName\('chkPaused'\)"
        }
    }

    Describe 'Project root resolution' {
        It 'resolves install root from explicit script root' {
            $root = Split-Path -Parent $PSScriptRoot
            Get-SmartGuardRoot -ScriptRoot $root | Should -Be $root
        }

        It 'defaults log path relative to install root' {
            $root = Split-Path -Parent $PSScriptRoot
            $cfg = Get-DefaultSmartGuardConfig -ScriptRoot $root
            $cfg.LogFile | Should -Be (Join-Path $root 'SmartGuard.log')
        }
    }

    Describe 'Toast AppUserModelId' {
        It 'returns stable app id' {
            Get-SmartGuardToastAppId | Should -Be 'Tools.SmartGuard.Guardian'
        }

        It 'registers display metadata in registry' {
            $root = Split-Path -Parent $PSScriptRoot
            Register-SmartGuardAppUserModelId -ScriptRoot $root | Should -Be $true
            $appId = Get-SmartGuardToastAppId
            $regPath = "HKCU:\Software\Classes\AppUserModelId\$appId"
            Test-Path $regPath | Should -Be $true
            (Get-ItemProperty -LiteralPath $regPath -Name DisplayName -ErrorAction SilentlyContinue).DisplayName | Should -Not -BeNullOrEmpty
        }
    }

    Describe 'Functions loader' {
        It 'loads domain and infrastructure modules from layers' {
            Get-Command Get-ExpectedPlanGuid -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
            Get-Command Read-SmartGuardConfig -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
            Get-Command Invoke-PowerCfgCommand -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
        }

        It 'keeps Functions.ps1 as thin entry point' {
            $root = Split-Path -Parent $PSScriptRoot
            $lines = (Get-Content -LiteralPath (Join-Path $root 'lib\SmartGuard.Functions.ps1')).Count
            $lines | Should -BeLessThan 40
        }
    }

    Describe 'Phase 5 Inno installer' {
        It 'SmartGuard.iss includes signed publisher and install hooks' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match '#define MyAppPublisher "rainiva"'
            $iss | Should -Match 'https://github.com/rainiva/SmartGuard'
            $iss | Should -Match 'SetupIconFile='
            $iss | Should -Match '--skip-publish'
            $iss | Should -Match 'ShouldDeleteUserData'
            $iss | Should -Match 'license_zh-CN\.txt'
            $iss | Should -Match '\{app\}\\bin\\SmartGuard\.Engine\.exe'
        }

        It 'stops SmartGuard processes before install and uninstall' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'CloseApplications=no'
            $iss | Should -Match 'EnsureSmartGuardStopped'
            $iss | Should -Match 'restartreplace'
            $iss | Should -Match 'function PrepareToInstall'
            $iss | Should -Match 'StopSmartGuardProcesses'
            $iss | Should -Match 'CurPageID = wpReady'
            $iss | Should -Match 'if not SmartGuardProcessesStillRunning\(\) then'
            $iss | Should -Match 'SmartGuard Guardian'
            $iss | Should -Match 'schtasks.*/End'
            $iss | Should -Match 'taskkill.*/T'
            $iss | Should -Match 'findstr.*/C:"SmartGuard\.Tray\.exe"'
            $iss | Should -Not -Match 'Get-Process -Name SmartGuard\*'
            $iss | Should -Not -Match 'Get-CimInstance Win32_Process'
            $iss | Should -Not -Match 'CurStepChanged'
            $iss | Should -Match 'CurUninstallStepChanged'
            $iss | Should -Match 'SolidCompression=no'
        }

        It 'uninstall user-data choice uses InnerNotebook wizard per official InitializeUninstallProgressForm' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'procedure InitializeUninstallProgressForm'
            $iss | Should -Match 'UninstallProgressForm\.InnerNotebook'
            $iss | Should -Match 'UninstallProgressForm\.ShowModal'
            $iss | Should -Match 'TNewNotebookPage'
            $iss | Should -Match '保留配置与日志'
            $iss | Should -Match '删除配置与日志'
            $iss | Should -Match 'UninstallSilent'
            $iss | Should -Not -Match 'CreateCustomPage'
            $iss | Should -Not -Match 'CreateCustomForm'
            $iss | Should -Not -Match 'Parent := WizardForm'
        }

        It 'starts guardian after install before tray' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match '/Run /TN ""SmartGuard Guardian""'
            $iss | Should -Match '正在启动核心服务'
        }

        It 'allows only one installed SmartGuard instance' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'UsePreviousAppDir=yes'
            $iss | Should -Match 'GetExistingSmartGuardInstallPath'
            $iss | Should -Match 'wpSelectDir'
        }

        It 'bumps installer patch version on each build' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'installer\InstallVersion.ps1')
            Get-BumpedPatchVersion -Version '1.0.0' | Should -Be '1.0.1'
            Get-BumpedPatchVersion -Version '1.0.9' | Should -Be '1.0.10'

            $versionFile = Join-Path $TestDrive 'version.txt'
            Set-Content -LiteralPath $versionFile -Value '2.3.4' -Encoding ASCII -NoNewline
            Update-InstallerVersionFile -VersionFile $versionFile | Should -Be '2.3.5'
            (Get-Content -LiteralPath $versionFile -Raw).Trim() | Should -Be '2.3.5'
            Update-InstallerVersionFile -VersionFile $versionFile -SkipBump | Should -Be '2.3.5'
            (Get-Content -LiteralPath $versionFile -Raw).Trim() | Should -Be '2.3.5'
        }

        It 'Build-Installer.ps1 uses InstallVersion bump helper' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'installer\Build-Installer.ps1') -Raw -Encoding UTF8
            $content | Should -Match 'InstallVersion\.ps1'
            $content | Should -Match 'Update-InstallerVersionFile'
            $content | Should -Match 'SkipVersionBump'
        }

        It 'Build-Staging.ps1 publishes and validates staging layout' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'installer\Build-Staging.ps1') -Raw -Encoding UTF8
            $content | Should -Match 'Publish-All\.ps1'
            $content | Should -Match 'Test-InstallerStagingLayout'
            $content | Should -Match 'license_zh-CN\.txt'
        }

        It 'Test-InstallerStagingLayout fails when staging is incomplete' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'installer\InstallStaging.ps1')
            $empty = Join-Path $TestDrive 'empty-staging'
            New-Item -ItemType Directory -Path $empty -Force | Out-Null
            { Test-InstallerStagingLayout -StagingDir $empty } | Should -Throw
        }

        It 'Test-InstallerStagingLayout passes for minimal fake staging' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'installer\InstallStaging.ps1')
            $staging = Join-Path $TestDrive 'staging'
            New-InstallerFakeStaging -StagingDir $staging
            { Test-InstallerStagingLayout -StagingDir $staging } | Should -Not -Throw
        }
    }
}
