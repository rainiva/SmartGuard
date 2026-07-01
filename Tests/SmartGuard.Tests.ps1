Describe 'SmartGuard' {
    Describe 'Tray assets' {
        It 'bundled SmartGuard.ico exists for tray and installers' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'lib\SmartGuard.ico'
            Test-Path -LiteralPath $icon | Should -Be $true
            (Get-Item -LiteralPath $icon).Length | Should -BeGreaterThan 1000
        }

        It 'bundled SmartGuard.ico keeps enough 16px tray footprint' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'lib\SmartGuard.ico'
            $fill = python -c "from PIL import Image; import sys; ico=Image.open(sys.argv[1]); img=ico.ico.getimage((16,16)).convert('RGBA'); pixels=sum(1 for p in img.getdata() if p[3] > 8); print(f'{pixels / 256 * 100:.1f}')" $icon

            [double]$fill | Should -BeGreaterOrEqual 47.0
        }

        It 'bundled SmartGuard.ico keeps the 24px white lightning accent and a simplified 16px tray palette' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'lib\SmartGuard.ico'
            $metricsJson = python -c "from PIL import Image; import json, sys; ico=Image.open(sys.argv[1]); img24=ico.ico.getimage((24,24)).convert('RGBA'); img16=ico.ico.getimage((16,16)).convert('RGBA'); white24=sum(1 for r,g,b,a in img24.getdata() if a > 200 and r > 230 and g > 235 and b > 235); bright16=sum(1 for r,g,b,a in img16.getdata() if a > 32 and r >= 90 and g >= 170 and b >= 220); blue16=sum(1 for r,g,b,a in img16.getdata() if a > 32 and 60 <= r <= 170 and 120 <= g <= 220 and 170 <= b <= 255); white16=sum(1 for r,g,b,a in img16.getdata() if a > 200 and r > 230 and g > 235 and b > 235); print(json.dumps({'white24': white24, 'bright16': bright16, 'blue16': blue16, 'white16': white16}))" $icon
            $metrics = $metricsJson | ConvertFrom-Json

            $metrics.white24 | Should -BeGreaterThan 6
            $metrics.bright16 | Should -BeGreaterThan 55
            $metrics.blue16 | Should -BeGreaterThan 120
            $metrics.white16 | Should -BeLessOrEqual 4
        }

        It 'bundled SmartGuard.ico keeps a crisp 32px frame for taskbar and Explorer' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'lib\SmartGuard.ico'
            $metricsJson = python -c "from PIL import Image; import json, sys; ico=Image.open(sys.argv[1]); img32=ico.ico.getimage((32,32)).convert('RGBA'); white32=sum(1 for r,g,b,a in img32.getdata() if a > 200 and r > 230 and g > 235 and b > 235); bright32=sum(1 for r,g,b,a in img32.getdata() if a > 32 and r >= 90 and g >= 170 and b >= 220); alpha32={a for _,_,_,a in img32.getdata() if a > 0}; print(json.dumps({'white32': white32, 'bright32': bright32, 'alpha32': len(alpha32)}))" $icon
            $metrics = $metricsJson | ConvertFrom-Json

            $metrics.white32 | Should -BeGreaterThan 15
            $metrics.bright32 | Should -BeGreaterThan 150
            $metrics.alpha32 | Should -BeLessOrEqual 90
        }

        It 'bundled SmartGuard.ico keeps a compact 32px lightning silhouette for Explorer lists' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'lib\SmartGuard.ico'
            $metricsJson = python -c "from PIL import Image; import json, sys; ico=Image.open(sys.argv[1]); img32=ico.ico.getimage((32,32)).convert('RGBA'); white=[(x,y) for y in range(img32.height) for x in range(img32.width) if (lambda p: p[3] > 200 and p[0] > 230 and p[1] > 235 and p[2] > 235)(img32.getpixel((x, y)))]; xs=[p[0] for p in white]; ys=[p[1] for p in white]; print(json.dumps({'width': max(xs) - min(xs) + 1, 'height': max(ys) - min(ys) + 1}))" $icon
            $metrics = $metricsJson | ConvertFrom-Json

            $metrics.width | Should -BeGreaterOrEqual 5
            $metrics.width | Should -BeLessOrEqual 8
            $metrics.height | Should -BeGreaterOrEqual 9
            $metrics.height | Should -BeLessOrEqual 12
        }

        It 'bundled SmartGuard.ico keeps a tighter 32px battery shell for Explorer lists' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'lib\SmartGuard.ico'
            $metricsJson = python -c "from PIL import Image; import json, sys; ico=Image.open(sys.argv[1]); img32=ico.ico.getimage((32,32)).convert('RGBA'); shell=sum(1 for r,g,b,a in img32.getdata() if a > 200 and r > 200 and g > 220 and b > 230); fill=sum(1 for r,g,b,a in img32.getdata() if a > 200 and 60 < r < 130 and 120 < g < 190 and 150 < b < 230); print(json.dumps({'shell': shell, 'fill': fill}))" $icon
            $metrics = $metricsJson | ConvertFrom-Json

            $metrics.shell | Should -BeLessOrEqual 140
            $metrics.fill | Should -BeGreaterOrEqual 120
        }

        It 'does not ship Create-TrayIcon.ps1 and packaging project exists' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'lib\Create-TrayIcon.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\SmartGuard.Packaging.csproj') | Should -Be $true
        }
    }

    Describe 'Phase 7.4 settings xaml source of truth' {
        It 'ships committed SmartGuard.Settings.xaml without generation script' {
            $root = Split-Path -Parent $PSScriptRoot
            $xamlPath = Join-Path $root 'lib\SmartGuard.Settings.xaml'
            $writer = Join-Path $root 'lib\Write-SmartGuardSettingsXaml.ps1'
            Test-Path -LiteralPath $xamlPath | Should -Be $true
            Test-Path -LiteralPath $writer | Should -Be $false
            $xaml = Get-Content -LiteralPath $xamlPath -Raw -Encoding UTF8
            $xaml | Should -Match 'x:Name="tglPaused"'
            $xaml | Should -Match 'x:Name="tglAutoStart"'
        }

        It 'Settings project links lib xaml as WPF Page' {
            $root = Split-Path -Parent $PSScriptRoot
            $csproj = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Settings\SmartGuard.Settings.csproj') -Raw -Encoding UTF8
            $csproj | Should -Match 'lib\\SmartGuard\.Settings\.xaml'
            $csproj | Should -Match '<Page Include'
        }

        It 'Build-Staging does not regenerate settings xaml' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'installer\Build-Staging.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\SmartGuard.Packaging.csproj') | Should -Be $true
        }
    }

    Describe 'Phase 7.5 dotnet publish chain' {
        It 'build.cmd publishes all four desktop executables' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'build.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Engine\.csproj'
            $content | Should -Match 'SmartGuard\.Tray\.csproj'
            $content | Should -Match 'SmartGuard\.LogViewer\.csproj'
            $content | Should -Match 'SmartGuard\.Settings\.csproj'
            $content | Should -Match '--self-contained false'
            $content | Should -Match 'win-x64'
        }

        It 'Directory.Build.props pins framework-dependent publish defaults' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw -Encoding UTF8
            $content | Should -Match '<SelfContained>false</SelfContained>'
        }

        It 'Setup-All.cmd calls build.cmd directly and Publish-All.ps1 is removed' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Setup-All.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'build\.cmd'
            $content | Should -Not -Match 'Publish-All\.ps1'
            Test-Path -LiteralPath (Join-Path $root 'scripts\Publish-All.ps1') | Should -Be $false
        }

        It 'legacy per-project Publish scripts are removed' {
            $root = Split-Path -Parent $PSScriptRoot
            @(
                'scripts\Publish-Engine.ps1'
                'scripts\Publish-Tray.ps1'
                'scripts\Publish-LogViewer.ps1'
                'scripts\Publish-Settings.ps1'
            ) | ForEach-Object {
                Test-Path -LiteralPath (Join-Path $root $_) | Should -Be $false -Because $_
            }
        }
    }

    Describe 'Phase 7.6 documentation sync' {
        It 'README documents build.cmd and Phase 7 contract' {
            $root = Split-Path -Parent $PSScriptRoot
            $readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw -Encoding UTF8
            $readme | Should -Match 'build\.cmd'
            $readme | Should -Match 'PHASE-7-TASK-CONTRACT'
            $readme | Should -Not -Match 'Publish-Engine\.ps1'
        }

        It 'MIGRATION reflects Phase 7 complete and current test counts' {
            $root = Split-Path -Parent $PSScriptRoot
            $md = Get-Content -LiteralPath (Join-Path $root 'docs\MIGRATION.md') -Raw -Encoding UTF8
            $md | Should -Match '\*\*7\.6\*\*.*\*\*已完成\*\*'
            $md | Should -Match 'Phase 7 \*\*7\.1–7\.6 已完成\*\*'
            $md | Should -Match '\|\s*Pester.*\|\s*39\s*\|'
            $md | Should -Match 'build\.cmd'
            $md | Should -Not -Match 'scripts/Publish-Engine\.ps1'
        }

        It 'INNO contract Build-Staging uses build.cmd not Xaml generator' {
            $root = Split-Path -Parent $PSScriptRoot
            $contract = Get-Content -LiteralPath (Join-Path $root 'docs\INNO-INSTALLER-TASK-CONTRACT.md') -Raw -Encoding UTF8
            $contract | Should -Match 'build\.cmd'
            $contract | Should -Not -Match 'Write-SmartGuardSettingsXaml'
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

    Describe 'Phase 6.2 exe-only launchers' {
        It 'Start-Core.cmd launches engine exe without PowerShell' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Start-Core.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Engine\.exe'
            $content | Should -Not -Match 'powershell\.exe'
            $content | Should -Not -Match 'SmartGuard\.Core\.ps1'
        }

        It 'Start-Tray.cmd launches tray exe without PowerShell fallback' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Start-Tray.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Tray\.exe'
            $content | Should -Not -Match 'powershell\.exe'
            $content | Should -Not -Match 'SmartGuard\.Tray\.ps1'
        }

        It 'Restart-Tray.cmd launches tray exe without PowerShell' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Restart-Tray.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Tray\.exe'
            $content | Should -Not -Match 'powershell\.exe'
        }

        It 'ToastShortcutResolver does not fall back to powershell.exe' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Tray\Toast\ToastShortcutResolver.cs') -Raw -Encoding UTF8
            $content | Should -Not -Match 'powershell\.exe'
        }

    }

    Describe 'Phase 7.1 dev launchers cmd-only' {
        It 'does not ship PS dev launcher scripts at repo root' {
            $root = Split-Path -Parent $PSScriptRoot
            @(
                'Start-Core.ps1'
                'Register-AllTasks.ps1'
                'Restart-Tray.ps1'
                'Start-SmartGuard.ps1'
            ) | ForEach-Object {
                Test-Path -LiteralPath (Join-Path $root $_) | Should -Be $false -Because $_
            }
        }

        It 'Register-AllTasks.cmd registers via Engine install' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Register-AllTasks.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Engine\.exe'
            $content | Should -Match '--install'
            $content | Should -Not -Match 'powershell\.exe'
            $content | Should -Not -Match 'Register-SmartGuardTask\.ps1'
        }

        It 'Start-SmartGuard.cmd launches Start-Core.cmd without PowerShell' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Start-SmartGuard.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'Start-Core\.cmd'
            $content | Should -Not -Match 'powershell\.exe'
        }
    }

    Describe 'Phase 7.2 status cmd without PowerShell' {
        It 'Status.cmd shows startup log tail without powershell.exe' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Status.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.startup\.log'
            $content | Should -Not -Match 'powershell'
        }
    }

    Describe 'Phase 7.3 legacy dev scripts' {
        It 'does not ship Repair-Encoding helpers that recreate PS stack' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Repair-Encoding.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'Repair.cmd') | Should -Be $false
        }

        It 'Setup-All.cmd uses repo-relative paths and current registration flow' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Setup-All.cmd') -Raw -Encoding UTF8
            $content | Should -Match '%~dp0'
            $content | Should -Match 'build\.cmd'
            $content | Should -Match 'Register-AllTasks\.cmd'
            $content | Should -Match 'Run-Tests'
            $content | Should -Not -Match 'D:\\Project\\SmartGuard'
            $content | Should -Not -Match 'Repair-Encoding'
            $content | Should -Not -Match 'Register-Task\.cmd'
            $content | Should -Not -Match 'SmartGuard\.Core\.ps1'
        }

        It 'archives one-shot Migrate-RenameToSmartGuard script' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'scripts\Migrate-RenameToSmartGuard.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'scripts\archive\Migrate-RenameToSmartGuard.ps1') | Should -Be $true
        }

        It 'Run-Tests.cmd uses repo-relative path' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.cmd') -Raw -Encoding UTF8
            $content | Should -Match '%~dp0'
            $content | Should -Not -Match 'D:\\Project\\SmartGuard'
        }
    }

    Describe 'Phase 6.3 remove PS application stack' {
        It 'does not ship legacy PS application entry scripts' {
            $root = Split-Path -Parent $PSScriptRoot
            @(
                'lib\SmartGuard.Core.ps1'
                'lib\SmartGuard.Tray.ps1'
                'lib\SmartGuard.Settings.ps1'
                'lib\Show-LogViewer.ps1'
                'lib\SmartGuard.Functions.ps1'
                'Register-SmartGuardTask.ps1'
                'Register-TrayTask.ps1'
            ) | ForEach-Object {
                Test-Path -LiteralPath (Join-Path $root $_) | Should -Be $false -Because $_
            }
        }

        It 'ScheduledTaskRegistrar requires published exe files' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\ScheduledTaskRegistrar.cs') -Raw -Encoding UTF8
            $content | Should -Match 'FileNotFoundException'
            $content | Should -Not -Match 'SmartGuard\.Core\.ps1'
            $content | Should -Not -Match 'SmartGuard\.Tray\.ps1'
            $content | Should -Not -Match 'powershell\.exe'
        }

        It 'ExternalToolLauncher opens only published desktop exes' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Tray\Infrastructure.cs') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Settings\.exe'
            $content | Should -Not -Match 'SmartGuard\.Settings\.ps1'
            $content | Should -Not -Match 'Show-LogViewer\.ps1'
            $content | Should -Not -Match 'powershell\.exe'
        }
    }

    Describe 'Phase 5 Inno installer' {
        It 'upgrade path detects existing installation via registry' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'GetExistingSmartGuardInstallPath'
            $iss | Should -Match 'UsePreviousAppDir=yes'
            $iss | Should -Match 'HKCU'
            $iss | Should -Match 'HKLM'
            $iss | Should -Match 'RegQueryStringValue'
        }

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
            $iss | Should -Not -Match 'Register-SmartGuardTask\.ps1'
            $iss | Should -Not -Match 'Register-TrayTask\.ps1'
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
            $iss | Should -Match 'schtasks.*/Delete'
            $iss | Should -Match 'schtasks.*/End'
            $iss | Should -Match 'schtasks.*/Change.*/Disable'
            $iss | Should -Match 'taskkill\.exe'
            $iss | Should -Match 'tasklist /NH'
            $iss | Should -Match "Pos\('SmartGuard\.Tray\.exe'"
            $iss | Should -Not -Match 'Get-Process -Name SmartGuard\*'
            $iss | Should -Not -Match 'Get-CimInstance Win32_Process'
            $iss | Should -Not -Match 'CurStepChanged'
            $iss | Should -Match 'CurUninstallStepChanged'
            $iss | Should -Match 'SolidCompression=no'
        }

        It 'uninstall user-data choice prompts in InitializeUninstall with a Yes/No message box' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'function InitializeUninstall\(\): Boolean'
            $iss | Should -Match 'MsgBox\('
            $iss | Should -Match 'MB_YESNO'
            $iss | Should -Match '保留配置与日志'
            $iss | Should -Match '删除配置与日志'
            $iss | Should -Match 'UninstallSilent'
            $iss | Should -Match 'DeleteUserData := \(Choice = IDYES\)'
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

        It 'upgrade path re-registers tasks after install' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match '--install.*--skip-publish'
            $iss | Should -Match 'schtasks\.exe.*Run.*SmartGuard Guardian'
        }

        It 'installer staging and version logic are in C# packaging project' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'installer\Build-Staging.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'installer\Build-Installer.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'installer\InstallVersion.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'installer\InstallStaging.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\SmartGuard.Packaging.csproj') | Should -Be $true
        }
    }

    Describe 'Phase 8 architecture single source of truth' {
        It 'ARCHITECTURE-CONTRACT.md exists with governance sections' {
            $root = Split-Path -Parent $PSScriptRoot
            $contract = Join-Path $root 'docs\ARCHITECTURE-CONTRACT.md'
            Test-Path -LiteralPath $contract | Should -Be $true
            $content = Get-Content -LiteralPath $contract -Raw -Encoding UTF8
            $content | Should -Match '真源注册表'
            $content | Should -Match 'InstallRootResolver'
            $content | Should -Match 'GuardConfigRepository'
        }

        It 'AGENTS.md documents architecture and multi-source TDD rules' {
            $root = Split-Path -Parent $PSScriptRoot
            $agents = Get-Content -LiteralPath (Join-Path $root 'AGENTS.md') -Raw -Encoding UTF8
            $agents | Should -Match '### 8\. 架构真源'
            $agents | Should -Match '### 11\. 多真源治理专项 TDD'
        }

        It 'SmartGuard.Architecture.Tests project is wired in Run-Tests.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Match 'SmartGuard\.Architecture\.Tests'
            Test-Path -LiteralPath (Join-Path $root 'Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj') | Should -Be $true
        }

        It 'GitHub Actions workflow runs Run-Tests.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $workflow = Get-Content -LiteralPath (Join-Path $root '.github\workflows\test.yml') -Raw -Encoding UTF8
            $workflow | Should -Match 'Run-Tests\.ps1'
            $workflow | Should -Match 'windows-latest'
        }

        It 'installer log shortcut opens Settings logs page not LogViewer' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'SmartGuard\.Settings\.exe.*--page logs'
            $iss | Should -Not -Match '\{group\}.*日志.*SmartGuard\.LogViewer\.exe'
        }

        It 'Status.cmd and Inno use SmartGuard Guardian and SmartGuard Tray task names' {
            $root = Split-Path -Parent $PSScriptRoot
            $status = Get-Content -LiteralPath (Join-Path $root 'Status.cmd') -Raw -Encoding UTF8
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $status | Should -Match 'SmartGuard Guardian'
            $status | Should -Match 'SmartGuard Tray'
            $iss | Should -Match 'SmartGuard Guardian'
            $iss | Should -Match 'SmartGuard Tray'
        }
    }
}
