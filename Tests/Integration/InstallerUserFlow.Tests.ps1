#Requires -Version 5.1
<#
  真实用户流程：静默安装 -> 验证产物 -> 静默卸载（等价于向导点完安装/卸载，无自定义控件交互）。
  若卸载脚本 Access violation，本测试必须失败。
#>
$script:InstallerUserFlowRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $script:InstallerUserFlowRepoRoot 'scripts\Test-IsProcessElevated.ps1')
$installerFlowHandoff = Invoke-InstallerUserFlowSingleElevation `
    -RepoRoot $script:InstallerUserFlowRepoRoot `
    -TestScriptPath $PSCommandPath
if ($installerFlowHandoff.HandedOff) {
    if ($installerFlowHandoff.ExitCode -ne 0) {
        throw "Elevated installer integration run failed with exit code $($installerFlowHandoff.ExitCode)."
    }

    return
}

if ($installerFlowHandoff.ShouldSkip) {
    Write-Host "Skipping installer integration: $($installerFlowHandoff.SkipReason)" -ForegroundColor Yellow
    return
}

Describe 'Installer user flow (integration)' {
    BeforeAll {
        function Wait-SmartGuardPath {
            param(
                [string]$Path,
                [bool]$ShouldExist,
                [int]$TimeoutSeconds = 10
            )

            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            do {
                $exists = Test-Path -LiteralPath $Path
                if ($exists -eq $ShouldExist) {
                    return $true
                }

                Start-Sleep -Milliseconds 200
            } while ((Get-Date) -lt $deadline)

            return ((Test-Path -LiteralPath $Path) -eq $ShouldExist)
        }

        . (Join-Path $PSScriptRoot 'InstallerUserFlow.Helpers.ps1')
        if ([string]::IsNullOrWhiteSpace($script:InstallerUserFlowRepoRoot)) {
            $script:InstallerUserFlowRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
        }

        $repoRoot = $script:InstallerUserFlowRepoRoot
        Initialize-InstallerUserFlowContext -RepoRoot $repoRoot
        Stop-SmartGuardForInstallerTest
        $script:SetupExe = Ensure-InstallerBuilt
    }

    AfterAll {
        Stop-SmartGuardForInstallerTest
    }

    It 'silent install then silent uninstall exits 0' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot
            $install.ExitCode | Should -Be 0 -Because "install log: $($install.LogPath)"

            Test-Path -LiteralPath (Join-Path $installRoot 'bin\SmartGuard.Engine.exe') | Should -Be $true
            Test-Path -LiteralPath (Join-Path $installRoot 'bin\SmartGuard.Tray.exe') | Should -Be $true

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0 -Because "uninstall log: $($uninstall.LogPath)"
        }
        finally {
            Stop-SmartGuardForInstallerTest
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    It 'install registers guardian task with --root at install path' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot
            $install.ExitCode | Should -Be 0

            $xml = schtasks /Query /TN 'SmartGuard Guardian' /XML 2>&1 | Out-String
            $xml | Should -Match '--root'
            $xml.Contains($installRoot) | Should -Be $true

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0
        }
        finally {
            Stop-SmartGuardForInstallerTest
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    It 'install payload does not ship Register ps1 scripts' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot
            $install.ExitCode | Should -Be 0

            Test-Path -LiteralPath (Join-Path $installRoot 'Register-SmartGuardTask.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $installRoot 'Register-TrayTask.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $installRoot 'lib\SmartGuard.Core.ps1') | Should -Be $false

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0
        }
        finally {
            Stop-SmartGuardForInstallerTest
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    It 'install registers tray task with --root at install path' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot
            $install.ExitCode | Should -Be 0

            $xml = schtasks /Query /TN 'SmartGuard Tray' /XML 2>&1 | Out-String
            $xml | Should -Match 'SmartGuard\.Tray\.exe'
            $xml | Should -Match '--root'
            $xml.Contains($installRoot) | Should -Be $true

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0
        }
        finally {
            Stop-SmartGuardForInstallerTest
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    It 'upgrade install preserves user data files in install root' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        $markerPath = Join-Path $installRoot '.sg-upgrade-marker'
        try {
            $first = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot
            $first.ExitCode | Should -Be 0

            $configPath = Join-Path $installRoot 'SmartGuard.config.json'
            $deadline = (Get-Date).AddSeconds(45)
            while (-not (Test-Path -LiteralPath $configPath) -and (Get-Date) -lt $deadline) {
                Start-Sleep -Seconds 1
            }
            Test-Path -LiteralPath $configPath | Should -Be $true

            'keep-me-on-upgrade' | Set-Content -LiteralPath $markerPath -Encoding UTF8
            Stop-SmartGuardForInstallerTest

            $second = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -PreserveExisting
            $second.ExitCode | Should -Be 0

            Test-Path -LiteralPath $configPath | Should -Be $true
            Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | Should -Match 'keep-me-on-upgrade'

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0
        }
        finally {
            Stop-SmartGuardForInstallerTest
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    It 'repair install removes legacy markers, loose assets, logviewer files, and legacy shell surfaces' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        $legacyLib = Join-Path $installRoot 'lib'
        $legacyBin = Join-Path $installRoot 'bin'
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        $programsDir = Join-Path ([Environment]::GetFolderPath([System.Environment+SpecialFolder]::ApplicationData)) 'Microsoft\Windows\Start Menu\Programs'
        $legacyGroupDir = Join-Path $programsDir 'SmartGuard'
        $trayShortcutName = "SmartGuard $([char]0x6258)$([char]0x76D8).lnk"
        $settingsShortcutName = "SmartGuard $([char]0x8BBE)$([char]0x7F6E).lnk"
        $logsShortcutName = "SmartGuard $([char]0x65E5)$([char]0x5FD7).lnk"
        $legacyUninstallShortcutName = "$([char]0x5378)$([char]0x8F7D) SmartGuard.lnk"
        $newTrayShortcut = Join-Path $programsDir $trayShortcutName
        $newSettingsShortcut = Join-Path $programsDir $settingsShortcutName
        $newLogsShortcut = Join-Path $programsDir $logsShortcutName
        $legacyGroupShortcuts = @(
            (Join-Path $legacyGroupDir $trayShortcutName),
            (Join-Path $legacyGroupDir $settingsShortcutName),
            (Join-Path $legacyGroupDir $logsShortcutName),
            (Join-Path $legacyGroupDir $legacyUninstallShortcutName)
        )
        try {
            $first = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot
            $first.ExitCode | Should -Be 0

            [System.IO.Directory]::CreateDirectory($legacyLib) | Out-Null
            [System.IO.Directory]::CreateDirectory($legacyBin) | Out-Null
            [System.IO.Directory]::CreateDirectory($legacyGroupDir) | Out-Null

            [System.IO.File]::WriteAllText((Join-Path $legacyLib '.smartguard-toast-shortcut'), 'legacy-shell-marker', $utf8NoBom)
            [System.IO.File]::WriteAllText((Join-Path $legacyLib '.smartguard-toast-aumid'), 'legacy-aumid-marker', $utf8NoBom)
            [System.IO.File]::WriteAllText((Join-Path $legacyLib 'SmartGuard.Settings.xaml'), '<Window />', $utf8NoBom)
            Copy-Item -LiteralPath (Join-Path $repoRoot 'assets\branding\SmartGuard.ico') -Destination (Join-Path $legacyLib 'SmartGuard.ico') -Force

            foreach ($legacyName in @(
                'SmartGuard.LogViewer.exe',
                'SmartGuard.LogViewer.dll',
                'SmartGuard.LogViewer.deps.json',
                'SmartGuard.LogViewer.runtimeconfig.json',
                'SmartGuard.LogViewer.pdb'
            )) {
                [System.IO.File]::WriteAllText((Join-Path $legacyBin $legacyName), 'legacy-logviewer', $utf8NoBom)
            }

            foreach ($path in @($newTrayShortcut, $newSettingsShortcut, $newLogsShortcut) + $legacyGroupShortcuts) {
                $directory = Split-Path -Parent $path
                if (-not [string]::IsNullOrWhiteSpace($directory)) {
                    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
                }
                [System.IO.File]::WriteAllBytes($path, [byte[]]@())
            }

            Remove-Item -LiteralPath $newTrayShortcut, $newSettingsShortcut, $newLogsShortcut -Force -ErrorAction SilentlyContinue
            Stop-SmartGuardForInstallerTest

            $repair = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -PreserveExisting
            $repair.ExitCode | Should -Be 0

            Test-Path -LiteralPath $legacyLib | Should -Be $false
            Test-Path -LiteralPath (Join-Path $legacyBin 'SmartGuard.LogViewer.exe') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $legacyBin 'SmartGuard.LogViewer.dll') | Should -Be $false
            foreach ($path in $legacyGroupShortcuts) {
                Wait-SmartGuardPath -Path $path -ShouldExist $false | Should -Be $true -Because $path
            }
            Wait-SmartGuardPath -Path $newTrayShortcut -ShouldExist $true | Should -Be $true
            Wait-SmartGuardPath -Path $newSettingsShortcut -ShouldExist $true | Should -Be $true
            Wait-SmartGuardPath -Path $newLogsShortcut -ShouldExist $true | Should -Be $true

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0
        }
        finally {
            Stop-SmartGuardForInstallerTest
            foreach ($path in @($newTrayShortcut, $newSettingsShortcut, $newLogsShortcut)) {
                if (Test-Path -LiteralPath $path) {
                    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
                }
            }
            if (Test-Path -LiteralPath $legacyGroupDir) {
                Remove-Item -LiteralPath $legacyGroupDir -Recurse -Force -ErrorAction SilentlyContinue
            }
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
