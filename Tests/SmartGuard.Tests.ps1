Describe 'SmartGuard' {
    Describe 'Tray assets' {
        It 'canonical SmartGuard.ico lives under assets branding' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            Test-Path -LiteralPath $icon | Should -Be $true
            (Get-Item -LiteralPath $icon).Length | Should -BeGreaterThan 1000
        }

        It 'canonical SmartGuard.ico fills enough 24px taskbar canvas width' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            $widthPct = python -c "from PIL import Image; import sys; ico=Image.open(sys.argv[1]); img=ico.ico.getimage((24,24)).convert('RGBA'); bbox=img.getchannel('A').getbbox(); print(f'{(bbox[2]-bbox[0])/24*100:.1f}')" $icon

            [double]$widthPct | Should -BeGreaterOrEqual 88.0
        }

        It 'canonical SmartGuard.ico fills enough 16px taskbar canvas width' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            $widthPct = python -c "from PIL import Image; import sys; ico=Image.open(sys.argv[1]); img=ico.ico.getimage((16,16)).convert('RGBA'); bbox=img.getchannel('A').getbbox(); print(f'{(bbox[2]-bbox[0])/16*100:.1f}')" $icon

            [double]$widthPct | Should -BeGreaterOrEqual 90.0
        }

        It 'canonical SmartGuard.ico keeps enough 16px tray footprint' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            $fill = python -c "from PIL import Image; import sys; ico=Image.open(sys.argv[1]); img=ico.ico.getimage((16,16)).convert('RGBA'); pixels=sum(1 for p in img.getdata() if p[3] > 8); print(f'{pixels / 256 * 100:.1f}')" $icon

            [double]$fill | Should -BeGreaterOrEqual 47.0
        }

        It 'canonical SmartGuard.ico keeps the 24px white lightning accent and a simplified 16px tray palette' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            $metricsJson = python -c "from PIL import Image; import json, sys; ico=Image.open(sys.argv[1]); img24=ico.ico.getimage((24,24)).convert('RGBA'); img16=ico.ico.getimage((16,16)).convert('RGBA'); white24=sum(1 for r,g,b,a in img24.getdata() if a > 200 and r > 230 and g > 235 and b > 235); bright16=sum(1 for r,g,b,a in img16.getdata() if a > 32 and r >= 90 and g >= 170 and b >= 220); blue16=sum(1 for r,g,b,a in img16.getdata() if a > 32 and 60 <= r <= 170 and 120 <= g <= 220 and 170 <= b <= 255); white16=sum(1 for r,g,b,a in img16.getdata() if a > 200 and r > 230 and g > 235 and b > 235); print(json.dumps({'white24': white24, 'bright16': bright16, 'blue16': blue16, 'white16': white16}))" $icon
            $metrics = $metricsJson | ConvertFrom-Json

            $metrics.white24 | Should -BeGreaterThan 6
            $metrics.bright16 | Should -BeGreaterThan 30
            $metrics.blue16 | Should -BeGreaterThan 120
            $metrics.white16 | Should -BeLessOrEqual 4
        }

        It 'canonical SmartGuard.ico keeps a crisp 32px frame for taskbar and Explorer' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            $metricsJson = python -c "from PIL import Image; import json, sys; ico=Image.open(sys.argv[1]); img32=ico.ico.getimage((32,32)).convert('RGBA'); bbox=img32.getchannel('A').getbbox(); white32=sum(1 for r,g,b,a in img32.getdata() if a > 200 and r > 230 and g > 235 and b > 235); alpha32={a for _,_,_,a in img32.getdata() if a > 0}; print(json.dumps({'white32': white32, 'widthPct': (bbox[2]-bbox[0])/32*100, 'alpha32': len(alpha32)}))" $icon
            $metrics = $metricsJson | ConvertFrom-Json

            $metrics.white32 | Should -BeGreaterThan 15
            $metrics.widthPct | Should -BeGreaterOrEqual 88.0
            $metrics.alpha32 | Should -BeLessOrEqual 90
        }

        It 'canonical SmartGuard.ico keeps readable 32px lightning and battery mass' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            $metricsJson = python -c "from PIL import Image; import json, sys; ico=Image.open(sys.argv[1]); img32=ico.ico.getimage((32,32)).convert('RGBA'); white=[(x,y) for y in range(img32.height) for x in range(img32.width) if (lambda p: p[3] > 200 and p[0] > 230 and p[1] > 235 and p[2] > 235)(img32.getpixel((x, y)))]; xs=[p[0] for p in white]; ys=[p[1] for p in white]; shell=sum(1 for r,g,b,a in img32.getdata() if a > 200 and r > 200 and g > 220 and b > 230); fill=sum(1 for r,g,b,a in img32.getdata() if a > 200 and 60 < r < 130 and 120 < g < 190 and 150 < b < 230); print(json.dumps({'width': max(xs) - min(xs) + 1, 'height': max(ys) - min(ys) + 1, 'shell': shell, 'fill': fill}))" $icon
            $metrics = $metricsJson | ConvertFrom-Json

            $metrics.width | Should -BeGreaterOrEqual 5
            $metrics.height | Should -BeGreaterOrEqual 9
            $metrics.shell | Should -BeLessOrEqual 220
            $metrics.fill | Should -BeGreaterOrEqual 120
        }

        It 'canonical SmartGuard.ico keeps vertical safe margins in taskbar and window frames' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            $metricsJson = python -c "from PIL import Image; import json, sys; ico=Image.open(sys.argv[1]); sizes=(24,32,48,64); data={}; 
for size in sizes:
    img=ico.ico.getimage((size,size)).convert('RGBA')
    bbox=img.getchannel('A').getbbox()
    data[str(size)]={'top': bbox[1], 'bottom': size - bbox[3], 'left': bbox[0], 'right': size - bbox[2]}
print(json.dumps(data))" $icon
            $metrics = $metricsJson | ConvertFrom-Json

            foreach ($size in '24', '32', '48', '64') {
                $metrics.$size.top | Should -BeGreaterOrEqual 1 -Because "${size}px frame must keep at least 1px transparent top margin to avoid visual clipping"
                $metrics.$size.bottom | Should -BeGreaterOrEqual 1 -Because "${size}px frame must keep at least 1px transparent bottom margin to avoid visual clipping"
            }
        }

        It 'does not ship Create-TrayIcon.ps1 and packaging project exists' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'lib\Create-TrayIcon.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\SmartGuard.Packaging.csproj') | Should -Be $true
        }
    }

    Describe 'Phase 7.4 settings xaml source of truth' {
        It 'settings project owns committed SmartGuard.Settings.xaml and retires loose runtime copies' {
            $root = Split-Path -Parent $PSScriptRoot
            $xamlPath = Join-Path $root 'src\SmartGuard.Settings\SmartGuard.Settings.xaml'
            $writer = Join-Path $root 'lib\Write-SmartGuardSettingsXaml.ps1'
            Test-Path -LiteralPath $xamlPath | Should -Be $true
            Test-Path -LiteralPath $writer | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'lib\SmartGuard.Settings.xaml') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'lib\Themes\Light.xaml') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'lib\Themes\Dark.xaml') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'lib\SmartGuard.ico') | Should -Be $false
            $xaml = Get-Content -LiteralPath $xamlPath -Raw -Encoding UTF8
            $xaml | Should -Match 'x:Name="tglPaused"'
            $xaml | Should -Match 'x:Name="tglAutoStart"'
        }

        It 'Settings project uses project-local xaml and themes as WPF pages' {
            $root = Split-Path -Parent $PSScriptRoot
            $csproj = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Settings\SmartGuard.Settings.csproj') -Raw -Encoding UTF8
            $csproj | Should -Match '<Page Include="SmartGuard\.Settings\.xaml"'
            $csproj | Should -Match '<Page Include="Themes\\Light\.xaml"'
            $csproj | Should -Match '<Page Include="Themes\\Dark\.xaml"'
            $csproj | Should -Not -Match 'lib\\SmartGuard\.Settings\.xaml'
            $csproj | Should -Not -Match 'lib\\Themes\\'
        }

        It 'Build-Staging does not require loose settings xaml payloads' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'installer\Build-Staging.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\SmartGuard.Packaging.csproj') | Should -Be $true
        }
    }

    Describe 'Phase 7.5 dotnet publish chain' {
        It 'build.cmd delegates publish to SmartGuard.Cli' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'build.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'publish'
            $content | Should -Not -Match 'dotnet publish'
        }

        It 'DesktopDistributionManifest owns the desktop publish and payload lists' {
            $root = Split-Path -Parent $PSScriptRoot
            $manifest = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Distribution\DesktopDistributionManifest.cs') -Raw -Encoding UTF8
            $publisher = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Build\DesktopSuitePublisher.cs') -Raw -Encoding UTF8
            $copier = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Staging\PayloadCopier.cs') -Raw -Encoding UTF8
            $layout = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Staging\StagingLayout.cs') -Raw -Encoding UTF8
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $slnx = Get-Content -LiteralPath (Join-Path $root 'SmartGuard.slnx') -Raw -Encoding UTF8
            $settingsProject = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Settings\SmartGuard.Settings.csproj') -Raw -Encoding UTF8
            $settingsTests = Get-Content -LiteralPath (Join-Path $root 'Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj') -Raw -Encoding UTF8
            $manifest | Should -Match 'PublishedProjects'
            $manifest | Should -Match 'PayloadEntries'
            $manifest | Should -Match 'InstallerProcesses'
            $manifest | Should -Not -Match 'SmartGuard\.LogViewer\.csproj'
            $manifest | Should -Not -Match 'LogViewerExeFileName'
            $publisher | Should -Match 'DesktopDistributionManifest\.PublishedProjects'
            $copier | Should -Match 'DesktopDistributionManifest\.PayloadEntries'
            $layout | Should -Match 'DesktopDistributionManifest\.RequiredStagingPaths'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Files\.iss\.inc'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Processes\.iss\.inc'
            $iss | Should -Not -Match 'SmartGuard\.LogViewer\.exe'
            $slnx | Should -Not -Match 'src/SmartGuard\.LogViewer/SmartGuard\.LogViewer\.csproj'
            $settingsProject | Should -Not -Match 'SmartGuard\.LogViewer\.csproj'
            $settingsTests | Should -Not -Match 'SmartGuard\.LogViewer\.csproj'
            $publisher | Should -Match '--self-contained false'
            $publisher | Should -Match 'win-x64'
        }

        It 'Directory.Build.props pins framework-dependent publish defaults' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw -Encoding UTF8
            $content | Should -Match '<SelfContained>false</SelfContained>'
        }

        It 'Setup-All.cmd is removed in favor of documented SmartGuard.Cli workflow' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Setup-All.cmd') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'scripts\Publish-All.ps1') | Should -Be $false
        }

        It 'runtime error messages must not reference removed Publish-All.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            @(
                'Tests\Integration\SmartGuardStop.ps1'
                'Tests\Integration\TrayCoreUserFlow.Helpers.ps1'
                'Tests\Integration\InstallerUserFlow.Helpers.ps1'
                'Start-Core.cmd'
                'Debug-Engine.cmd'
                'Register-AllTasks.cmd'
            ) | ForEach-Object {
                $content = Get-Content -LiteralPath (Join-Path $root $_) -Raw -Encoding UTF8
                $content | Should -Not -Match 'Publish-All\.ps1' -Because $_
            }
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
        It 'README documents build.cmd and canonical architecture docs' {
            $root = Split-Path -Parent $PSScriptRoot
            $readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw -Encoding UTF8
            $readme | Should -Match 'build\.cmd'
            $readme | Should -Match 'ARCHITECTURE-CONTRACT'
            $readme | Should -Not -Match 'Publish-Engine\.ps1'
            $readme | Should -Match 'docs/archive/README\.md'
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
            $install = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Cli\InstallCommands.cs') -Raw -Encoding UTF8
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
        It 'Start-Core.cmd delegates to SmartGuard.Cli start guardian' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Start-Core.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'start guardian'
            $content | Should -Not -Match 'powershell\.exe'
            $content | Should -Not -Match 'SmartGuard\.Core\.ps1'
        }

        It 'Debug-Engine.cmd delegates to SmartGuard.Cli start engine' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Debug-Engine.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'start engine'
            $content | Should -Not -Match 'powershell\.exe'
        }

        It 'Start-Tray.cmd delegates to SmartGuard.Cli start tray' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Start-Tray.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'start tray'
            $content | Should -Not -Match 'powershell\.exe'
            $content | Should -Not -Match 'SmartGuard\.Tray\.ps1'
        }

        It 'Restart-Tray.cmd delegates to SmartGuard.Cli restart tray' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Restart-Tray.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'restart tray'
            $content | Should -Not -Match 'powershell\.exe'
        }

        It 'DesktopShellRegistration does not fall back to powershell.exe' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Shell\DesktopShellRegistration.cs') -Raw -Encoding UTF8
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

        It 'Register-AllTasks.cmd registers via SmartGuard.Cli install' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Register-AllTasks.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'install'
            $content | Should -Not -Match 'powershell\.exe'
            $content | Should -Not -Match 'Register-SmartGuardTask\.ps1'
        }

        It 'Start-SmartGuard.cmd is removed in favor of SmartGuard.Cli' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Start-SmartGuard.cmd') | Should -Be $false
        }
    }

    Describe 'Phase 7.2 status via SmartGuard.Cli' {
        It 'Status.cmd is removed and StatusReporter is the status true source' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Status.cmd') | Should -Be $false
            $reporter = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\StatusReporter.cs') -Raw -Encoding UTF8
            $reporter | Should -Match 'ScheduledTaskRegistrar\.TaskNames'
            $reporter | Should -Match 'StartupLogFile'
            $cli = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Cli\CliRouter.cs') -Raw -Encoding UTF8
            $cli | Should -Match 'StatusReporter\.FormatStatus'
        }
    }

    Describe 'Phase 7.3 legacy dev scripts' {
        It 'does not ship Repair-Encoding helpers that recreate PS stack' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Repair-Encoding.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'Repair.cmd') | Should -Be $false
        }

        It 'Setup-All.cmd is not shipped at repo root' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Setup-All.cmd') | Should -Be $false
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

    Describe 'Workspace cleanup script' {
        It 'Clean-Workspace.ps1 supports DryRun without deleting build output' {
            $root = Split-Path -Parent $PSScriptRoot
            $script = Join-Path $root 'scripts\Clean-Workspace.ps1'
            Test-Path -LiteralPath $script | Should -Be $true

            $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $script -DryRun -AllLocal 2>&1 | Out-String
            $output | Should -Match 'Size before:'
            $output | Should -Match 'Estimated reclaimable:'
            $output | Should -Match 'Worktrees under .worktrees/'
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
            $tray = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Tray\Infrastructure.cs') -Raw -Encoding UTF8
            $tray | Should -Match 'SettingsMainPageLauncher\.Open'
            $tray | Should -Match 'OpenLogsPage'
            $tray | Should -Match 'SettingsLogsPageLauncher\.Open'
            $tray | Should -Not -Match 'OpenLogViewer'
            $tray | Should -Not -Match 'SmartGuard\.Settings\.ps1'
            $tray | Should -Not -Match 'Show-LogViewer\.ps1'
            $tray | Should -Not -Match 'powershell\.exe'
            $mainLauncher = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\SettingsMainPageLauncher.cs') -Raw -Encoding UTF8
            $mainLauncher | Should -Match 'SmartGuardPaths\.SettingsExe'
            $logsLauncher = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\SettingsLogsPageLauncher.cs') -Raw -Encoding UTF8
            $logsLauncher | Should -Match 'SmartGuardPaths\.SettingsExe'
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
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $iss | Should -Match '#define MyAppPublisher "rainiva"'
            $iss | Should -Match 'https://github.com/rainiva/SmartGuard'
            $iss | Should -Match 'SetupIconFile='
            $iss | Should -Match 'ShouldDeleteUserData'
            $iss | Should -Match 'license_zh-CN\.txt'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $writer | Should -Match '--skip-publish'
            $writer | Should -Match '\{#EngineExeName\}'
            $iss | Should -Not -Match 'Register-SmartGuardTask\.ps1'
            $iss | Should -Not -Match 'Register-TrayTask\.ps1'
        }

        It 'stops SmartGuard processes before install and uninstall' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $manifest = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Distribution\DesktopDistributionManifest.cs') -Raw -Encoding UTF8
            $iss | Should -Match 'CloseApplications=no'
            $iss | Should -Match 'EnsureSmartGuardStopped'
            $iss | Should -Match '\[InstallDelete\]'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.LegacyCleanup\.iss\.inc'
            $iss | Should -Match 'function PrepareToInstall'
            $iss | Should -Match 'procedure StopSmartGuardProcesses'
            $iss | Should -Match 'CurPageID = wpReady'
            $iss | Should -Match 'if not SmartGuardProcessesStillRunning\(\) then'
            $iss | Should -Match 'tasklist /NH'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Processes\.iss\.inc'
            $iss | Should -Not -Match "Pos\('SmartGuard\.Tray\.exe'"
            $manifest | Should -Match 'SmartGuardPaths\.EngineExeFileName'
            $manifest | Should -Match 'SmartGuardPaths\.TrayExeFileName'
            $manifest | Should -Match 'SmartGuardPaths\.SettingsExeFileName'
            $manifest | Should -Not -Match 'SmartGuard\.LogViewer\.csproj'
            $manifest | Should -Not -Match 'LogViewerExeFileName'
            $iss | Should -Not -Match 'Get-Process -Name SmartGuard\*'
            $iss | Should -Not -Match 'Get-CimInstance Win32_Process'
            $iss | Should -Not -Match 'CurStepChanged'
            $iss | Should -Match 'CurUninstallStepChanged'
            $iss | Should -Match 'SolidCompression=no'
            $stopBlock = [regex]::Match($iss, '(?s)procedure StopSmartGuardProcesses.*?end;').Value
            $stopBlock | Should -Not -Match 'schtasks /End'
            $stopBlock | Should -Not -Match 'taskkill\.exe'
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
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $writer | Should -Match '\{#GuardianTaskName\}'
            $writer | Should -Match '正在启动核心服务'
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
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $writer | Should -Match '--install.*--skip-publish'
            $writer | Should -Match 'schtasks\.exe.*Run.*\{#GuardianTaskName\}'
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
        It 'active scripts do not advertise retired lib icon outputs' {
            $root = Split-Path -Parent $PSScriptRoot
            $script = Get-Content -LiteralPath (Join-Path $root 'scripts\generate-smartguard-icons.py') -Raw -Encoding UTF8
            $script | Should -Not -Match 'save_ico\(LIB_DIR / "SmartGuard\.ico"'
            $script | Should -Not -Match 'lib/SmartGuard\.ico'
        }

        It 'historical task-contract and diagnosis docs live under docs/archive instead of top-level docs' {
            $root = Split-Path -Parent $PSScriptRoot
            $archiveRoot = Join-Path $root 'docs\archive'
            $historical = @(Get-ChildItem -LiteralPath $archiveRoot -File -Filter '*.md' | Where-Object {
                $_.BaseName -ne 'README'
            })
            $historical.Count | Should -BeGreaterThan 0

            foreach ($doc in $historical) {
                Test-Path -LiteralPath (Join-Path $root ('docs\' + $doc.Name)) | Should -Be $false
                Test-Path -LiteralPath $doc.FullName | Should -Be $true
            }
        }

        It 'archive README declares that canonical docs override historical snapshots' {
            $root = Split-Path -Parent $PSScriptRoot
            $readme = Get-Content -LiteralPath (Join-Path $root 'docs\archive\README.md') -Raw -Encoding UTF8
            $readme | Should -Match 'historical'
            $readme | Should -Match 'ARCHITECTURE-CONTRACT'
            $readme | Should -Match 'MULTI-SOURCE-GOVERNANCE-ACCEPTANCE'
            $readme | Should -Match 'single-entry-single-source'
        }

        It 'ARCHITECTURE-CONTRACT.md exists with governance sections' {
            $root = Split-Path -Parent $PSScriptRoot
            $contract = Join-Path $root 'docs\ARCHITECTURE-CONTRACT.md'
            Test-Path -LiteralPath $contract | Should -Be $true
            $content = Get-Content -LiteralPath $contract -Raw -Encoding UTF8
            $content | Should -Match '真源注册表'
            $content | Should -Match 'InstallRootResolver'
            $content | Should -Match 'GuardConfigRepository'
        }

        It 'single-entry single-source rule doc exists as the short governance entrypoint' {
            $root = Split-Path -Parent $PSScriptRoot
            $ruleDoc = Join-Path $root 'docs\architecture\single-entry-single-source.md'
            Test-Path -LiteralPath $ruleDoc | Should -Be $true
            $content = Get-Content -LiteralPath $ruleDoc -Raw -Encoding UTF8
            $content | Should -Match 'canonical owner'
            $content | Should -Match 'fallback'
            $content | Should -Match 'marker'
            $content | Should -Match 'DesktopShellRegistration'
            $content | Should -Match 'DesktopDistributionManifest'
            $content | Should -Match 'SmartGuard\.Settings\.exe --page logs'
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
            $dotnetTests = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Match 'Invoke-SmartGuardDotNetTests\.ps1'
            $dotnetTests | Should -Match 'SmartGuard\.Architecture\.Tests'
            Test-Path -LiteralPath (Join-Path $root 'Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj') | Should -Be $true
        }

        It 'installer integration helper builds via SmartGuard.Packaging not Build-Installer.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $helper = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\InstallerUserFlow.Helpers.ps1') -Raw -Encoding UTF8
            $helper | Should -Match 'SmartGuard\.Packaging'
            $helper | Should -Match 'installer'
            $helper | Should -Not -Match 'Build-Installer\.ps1'
        }

        It 'GitHub Actions workflow runs Run-Tests.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $workflow = Get-Content -LiteralPath (Join-Path $root '.github\workflows\test.yml') -Raw -Encoding UTF8
            $workflow | Should -Match 'Run-Tests\.ps1'
            $workflow | Should -Match 'windows-latest'
        }

        It 'installer delegates shell surfaces to InstallCommands instead of owning [Icons]' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $writer | Should -Match '\{#CliExeName\}'
            $writer | Should -Match '--create-desktop-shortcut'
            $iss | Should -Not -Match '(?ms)^\[Icons\]'
            $iss | Should -Not -Match 'DefaultGroupName='
            $iss | Should -Not -Match 'DisableProgramGroupPage='
        }

        It 'DesktopShellRegistration publishes one start menu shortcut not three legacy entries' {
            $root = Split-Path -Parent $PSScriptRoot
            $shellOwner = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Shell\DesktopShellRegistration.cs') -Raw -Encoding UTF8
            $liveStartMenu = if ($shellOwner -match '(?s)GetStartMenuShortcuts\(string root\)\s*\{(.*?)\n  \}') { $Matches[1] } else { '' }
            $liveStartMenu | Should -Match 'SmartGuard\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 托盘\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 设置\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 日志\.lnk'
            $liveStartMenu | Should -Not -Match '--page logs'
        }

        It 'solution and docs do not reference SmartGuard.LogViewer.Tests or three live shell entries' {
            $root = Split-Path -Parent $PSScriptRoot
            $slnx = Get-Content -LiteralPath (Join-Path $root 'SmartGuard.slnx') -Raw -Encoding UTF8
            $settingsProject = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Settings\SmartGuard.Settings.csproj') -Raw -Encoding UTF8
            $slnx | Should -Not -Match 'SmartGuard\.LogViewer\.Tests'
            $settingsProject | Should -Not -Match 'InternalsVisibleTo Include="SmartGuard\.LogViewer\.Tests"'

            $shellOwner = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Shell\DesktopShellRegistration.cs') -Raw -Encoding UTF8
            $liveStartMenu = if ($shellOwner -match '(?s)GetStartMenuShortcuts\(string root\)\s*\{(.*?)\n  \}') { $Matches[1] } else { '' }
            $liveStartMenu | Should -Match 'SmartGuard\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 托盘\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 设置\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 日志\.lnk'

            # Repo truth gates exempt only docs/archive/, scripts/archive/, and docs/superpowers/plans/.
            $docHits = @(rg -n 'SmartGuard\.LogViewer\.Tests|SmartGuard 日志\.lnk|SmartGuard 设置\.lnk|SmartGuard 托盘\.lnk|lib\\SmartGuard\.ico|lib\\SmartGuard\.Settings\.xaml|\[Icons\].*日志|SmartGuard\.LogViewer\.exe' (Join-Path $root 'docs') 2>$null | Where-Object {
                ($_ -notmatch 'docs[\\/]+archive[\\/]') `
                    -and ($_ -notmatch 'docs[\\/]+superpowers[\\/]+plans[\\/]') `
                    -and ($_ -notmatch 'docs[\\/]+ARCHITECTURE-CONTRACT\.md') `
                    -and ($_ -notmatch 'docs[\\/]+MULTI-SOURCE-GOVERNANCE-ACCEPTANCE\.md') `
                    -and ($_ -notmatch 'docs[\\/]+architecture[\\/]+single-entry-single-source\.md')
            })
            $docHits.Count | Should -Be 0 -Because ($docHits -join [Environment]::NewLine)

            $scriptHits = @(rg -n 'lib\\SmartGuard\.ico|lib\\SmartGuard\.Settings\.xaml' (Join-Path $root 'scripts') 2>$null | Where-Object {
                $_ -notmatch 'scripts[\\/]+archive[\\/]'
            })
            $scriptHits.Count | Should -Be 0 -Because ($scriptHits -join [Environment]::NewLine)
        }

        It 'repo truth gates exempt only archive and plan paths, never filename heuristics' {
            $root = Split-Path -Parent $PSScriptRoot
            $suite = Get-Content -LiteralPath (Join-Path $root 'Tests\SmartGuard.Tests.ps1') -Raw -Encoding UTF8
            $historyGate = if ($suite -match "(?s)It 'historical task-contract and diagnosis docs live under docs/archive instead of top-level docs' \{(.*?)\r?\n        \}\r?\n\r?\n        It 'archive README declares that canonical docs override historical snapshots'") { $Matches[1] } else { '' }
            $historyGate | Should -Match 'Get-ChildItem'
            $historyGate | Should -Not -Match "'[A-Za-z0-9][^']*\.md'"

            $docGate = if ($suite -match '(?s)# Repo truth gates exempt.*?\$docHits\.Count') { $Matches[0] } else { '' }
            $docGate | Should -Match 'docs/archive/'
            $docGate | Should -Match 'docs/superpowers/plans/'
            $docGate | Should -Not -Match '\d{4}-\d{2}-\d{2}'

            $scriptGate = if ($suite -match '(?s)\$scriptHits = @\(rg.*?\$scriptHits\.Count') { $Matches[0] } else { '' }
            $scriptGate | Should -Match 'scripts.*archive'
        }

        It 'installer does not package or advertise SmartGuard.LogViewer.exe as a live entry' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Not -Match 'SmartGuard\.LogViewer\.exe'
        }

        It 'installer does not keep live exe or task literals once generated includes exist' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Constants\.iss\.inc'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $iss | Should -Not -Match '#define MyAppExeName "SmartGuard\.Tray\.exe"'
            $iss | Should -Not -Match 'UninstallDisplayIcon=\{app\}\\bin\\SmartGuard\.Tray\.exe'
            $iss | Should -Not -Match 'Filename: "\{app\}\\bin\\SmartGuard\.Engine\.exe"'
            $iss | Should -Not -Match 'Filename: "\{app\}\\bin\\SmartGuard\.Cli\.exe"'
            $iss | Should -Not -Match '/TN ""SmartGuard Guardian""'
        }

        It 'live scripts do not keep legacy SmartPowerPlan cleanup entrypoints' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'scripts\LegacyScheduledTaskNames.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'scripts\Unregister-LegacySmartPowerPlanTasks.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'scripts\archive\LegacyScheduledTaskNames.ps1') | Should -Be $true
            Test-Path -LiteralPath (Join-Path $root 'scripts\archive\Unregister-LegacySmartPowerPlanTasks.ps1') | Should -Be $true
        }

        It 'contract docs do not leave historical migration pointers in live guidance' {
            $root = Split-Path -Parent $PSScriptRoot
            $contract = Get-Content -LiteralPath (Join-Path $root 'docs\ARCHITECTURE-CONTRACT.md') -Raw -Encoding UTF8
            $acceptance = Get-Content -LiteralPath (Join-Path $root 'docs\MULTI-SOURCE-GOVERNANCE-ACCEPTANCE.md') -Raw -Encoding UTF8
            $contract | Should -Not -Match '见 MIGRATION'
            $contract | Should -Match 'generated/SmartGuard\.Distribution\.Constants\.iss\.inc'
            $contract | Should -Match 'LegacyScheduledTaskCatalog'
            $acceptance | Should -Match 'scripts/archive/'
            $acceptance | Should -Match 'SmartGuard\.Distribution\.Run\.iss\.inc'
        }

        It 'integration stop helpers delegate to Engine --uninstall' {
            $root = Split-Path -Parent $PSScriptRoot
            $shared = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\SmartGuardStop.ps1') -Raw -Encoding UTF8
            $tray = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Helpers.ps1') -Raw -Encoding UTF8
            $installer = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\InstallerUserFlow.Helpers.ps1') -Raw -Encoding UTF8
            $shared | Should -Match '--uninstall'
            $tray | Should -Match 'SmartGuardStop\.ps1'
            $installer | Should -Match 'SmartGuardStop\.ps1'
            $tray | Should -Not -Match 'schtasks /End'
            $installer | Should -Not -Match 'schtasks /End'
        }

        It 'StatusReporter and Inno use registrar task names' {
            $root = Split-Path -Parent $PSScriptRoot
            $status = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\StatusReporter.cs') -Raw -Encoding UTF8
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $status | Should -Match 'ScheduledTaskRegistrar'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Constants\.iss\.inc'
            $iss | Should -Not -Match 'SmartGuard Guardian'
            $writer | Should -Match 'ScheduledTaskRegistrar\.GuardianTaskName'
            $stopBlock = [regex]::Match($iss, '(?s)procedure StopSmartGuardProcesses.*?end;').Value
            $stopBlock | Should -Match '\\bin\\\{#EngineExeName\}'
            $stopBlock | Should -Not -Match 'SmartGuard\.Engine\.exe'
            $stopBlock | Should -Match '--uninstall'
        }

        It 'installer stop procedure consumes EngineExeName define instead of engine literal' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $stop = [regex]::Match($iss, '(?s)procedure StopSmartGuardProcesses\(\);.*?end;').Value
            $stop | Should -Match '\\bin\\\{#EngineExeName\}'
            $stop | Should -Not -Match 'SmartGuard\.Engine\.exe'
        }

        It 'closeout docs describe implementation-path grep instead of fake full-repo silence' {
            $root = Split-Path -Parent $PSScriptRoot
            $contract = Get-Content -LiteralPath (Join-Path $root 'docs\ARCHITECTURE-CONTRACT.md') -Raw -Encoding UTF8
            $acceptance = Get-Content -LiteralPath (Join-Path $root 'docs\MULTI-SOURCE-GOVERNANCE-ACCEPTANCE.md') -Raw -Encoding UTF8
            $overview = Get-Content -LiteralPath (Join-Path $root 'docs\architecture\single-entry-single-source.md') -Raw -Encoding UTF8

            $contract | Should -Match 'implementation-path grep'
            $acceptance | Should -Match 'docs/tests may mention retired literals only as assertions or contract text'
            $overview | Should -Match 'Windows consumers are allowed only when they point back to canonical exe or embedded icon chain'
        }

        It 'implementation-path grep blocks live literals and fallback icons without scanning docs/tests' {
            $root = Split-Path -Parent $PSScriptRoot
            $hits = @(rg -n "RootPath \+ '\\bin\\SmartGuard\.Engine\.exe'|LoadWinFormsIcon\(SystemIcons\.Shield\)|SystemIcons\.Shield|SmartGuardPaths\.BrandIcon|lib\\SmartGuard\.ico" `
                (Join-Path $root 'installer') (Join-Path $root 'src') (Join-Path $root 'scripts') --glob '!scripts/archive/**' 2>$null)
            $hits.Count | Should -Be 0 -Because ($hits -join [Environment]::NewLine)
        }
    }

    Describe 'Run-Tests optimization contract' {
        It 'Run-Tests.ps1 delegates dotnet tests to Invoke-SmartGuardDotNetTests.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Match 'Invoke-SmartGuardDotNetTests\.ps1'
            $runTests | Should -Match 'Invoke-SmartGuardDotNetTestSuite'
        }

        It 'Run-Tests.ps1 honors SMARTGUARD_TEST_TIER for integration gating' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Match 'SMARTGUARD_TEST_TIER'
            $runTests | Should -Match 'Get-SmartGuardTestTier'
        }

        It 'Tray core user flow does not spawn nested dotnet test' {
            $root = Split-Path -Parent $PSScriptRoot
            $flow = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Tests.ps1') -Raw -Encoding UTF8
            $flow | Should -Not -Match 'dotnet test'
        }

        It 'Invoke-RunTestsSingleElevation exits parent after successful UAC handoff' {
            $root = Split-Path -Parent $PSScriptRoot
            $elevation = Get-Content -LiteralPath (Join-Path $root 'scripts\Test-IsProcessElevated.ps1') -Raw -Encoding UTF8
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $fn = [regex]::Match($elevation, '(?s)function Invoke-RunTestsSingleElevation.*?(?=\r?\nfunction |\z)').Value
            $fn | Should -Match 'HandedOff\s*=\s*\$true'
            $fn | Should -Match 'LASTEXITCODE'
            $elevation | Should -Match 'function Get-WindowsPowerShellExePath'
            $fn | Should -Match '\$powershellExe\s*=\s*Get-WindowsPowerShellExePath'
            $fn | Should -Match 'Start-Process\s+-FilePath\s+\$powershellExe\s+-Verb\s+RunAs'
            $fn | Should -Match 'Unable to launch elevated Windows PowerShell'
            $runTests | Should -Match 'if\s*\(\s*\$handoff\.HandedOff\s*\)\s*\{\s*exit\s+\$handoff\.ExitCode'
        }

        It 'Test-SmartGuardInstallerIntegrationEnabled requires elevation to avoid repeated UAC' {
            $root = Split-Path -Parent $PSScriptRoot
            $helper = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1') -Raw -Encoding UTF8
            $fn = [regex]::Match($helper, '(?s)function Test-SmartGuardInstallerIntegrationEnabled.*?(?=\r?\nfunction |\z)').Value
            $fn | Should -Match 'Test-IsProcessElevated'
            $fn | Should -Not -Match "if\s*\(\s*\`$Tier\s*-eq\s*'nightly'\s*\)\s*\{\s*return\s+\`$true\s*\}"
        }

        It 'InstallerUserFlow.Tests.ps1 self-handoffs to one elevated Pester run when launched directly' {
            $root = Split-Path -Parent $PSScriptRoot
            $elevation = Get-Content -LiteralPath (Join-Path $root 'scripts\Test-IsProcessElevated.ps1') -Raw -Encoding UTF8
            $flow = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\InstallerUserFlow.Tests.ps1') -Raw -Encoding UTF8
            $elevation | Should -Match 'function Invoke-InstallerUserFlowSingleElevation'
            $elevation | Should -Match 'Invoke-Pester\s+-Path'
            $elevation | Should -Match 'SMARTGUARD_ELEVATED_RUN'
            $elevation | Should -Match '\$powershellExe\s*=\s*Get-WindowsPowerShellExePath'
            $elevation | Should -Match 'Start-Process\s+-FilePath\s+\$powershellExe\s+-Verb\s+RunAs'
            $elevation | Should -Match 'Unable to launch elevated Windows PowerShell'
            $flow | Should -Match 'Invoke-InstallerUserFlowSingleElevation'
            $flow | Should -Match '\$PSCommandPath'
            $flow | Should -Match '\$installerFlowHandoff\.HandedOff'
        }

        It 'dotnet test helper builds with no-restore before no-build test fanout' {
            $root = Split-Path -Parent $PSScriptRoot
            $helper = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1') -Raw -Encoding UTF8
            $helper | Should -Match 'dotnet build'
            $helper | Should -Match '--no-restore'
            $helper | Should -Match '--no-build'
        }

        It 'parallel dotnet test jobs pin UTF-8 encoding inside the background PowerShell process' {
            $root = Split-Path -Parent $PSScriptRoot
            $helper = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1') -Raw -Encoding UTF8
            $jobBlock = [regex]::Match($helper, '(?s)Start-Job\s+-ScriptBlock\s+\{.*?\}\s+-ArgumentList').Value
            $jobBlock | Should -Match '\[Console\]::OutputEncoding\s*=\s*\[System\.Text\.Encoding\]::UTF8'
            $jobBlock | Should -Match '\[Console\]::InputEncoding\s*=\s*\[System\.Text\.Encoding\]::UTF8'
            $jobBlock | Should -Match '\$OutputEncoding\s*=\s*\[System\.Text\.Encoding\]::UTF8'
            $jobBlock | Should -Match "DOTNET_CONSOLE_ENCODING\s*=\s*'utf-8'"
            $jobBlock | Should -Match "chcp\)\s*-notmatch\s*'65001'"
        }

        It 'InstallerUserFlow.Tests.ps1 re-resolves repo root in BeforeAll when script scope is empty' {
            $root = Split-Path -Parent $PSScriptRoot
            $flow = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\InstallerUserFlow.Tests.ps1') -Raw -Encoding UTF8
            $flow | Should -Match 'IsNullOrWhiteSpace\(\$script:InstallerUserFlowRepoRoot\)'
            $flow | Should -Match '\$script:InstallerUserFlowRepoRoot = \(Resolve-Path \(Join-Path \$PSScriptRoot ''\.\.\\\.\.''\)\)\.Path'
        }

        It 'Stop-SmartGuardProcesses avoids Engine --uninstall UAC when not elevated' {
            $root = Split-Path -Parent $PSScriptRoot
            $stop = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\SmartGuardStop.ps1') -Raw -Encoding UTF8
            $stop | Should -Match 'Stop-SmartGuardProcessesWithoutElevation'
            $stop | Should -Match 'if\s*\(\s*-not\s*\(\s*Test-IsProcessElevated\s*\)\s*\)'
        }
    }
}
