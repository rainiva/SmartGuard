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
    $elevatedResult = $installerFlowHandoff.PesterResult
    if (-not (Test-SmartGuardElevatedPesterResult -Result $elevatedResult)) {
        $elevatedError = if ($null -eq $elevatedResult) {
            'No structured result was returned.'
        }
        else {
            [string]$elevatedResult.Error
        }
        throw (
            "Elevated installer integration run failed with exit code " +
            "$($installerFlowHandoff.ExitCode): $elevatedError")
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

        . (Join-Path $PSScriptRoot 'Restore-SmartGuardScheduledTasks.ps1')
        $script:TaskSnapshot = Capture-SmartGuardScheduledTasks

        . (Join-Path $PSScriptRoot 'InstallerUserFlow.Helpers.ps1')
        if ([string]::IsNullOrWhiteSpace($script:InstallerUserFlowRepoRoot)) {
            $script:InstallerUserFlowRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
        }

        $repoRoot = $script:InstallerUserFlowRepoRoot
        Initialize-InstallerUserFlowContext -RepoRoot $repoRoot
        Stop-SmartGuardForInstallerTest
        $script:SetupExe = Ensure-InstallerBuilt
        $script:ProgramsDir = Get-SmartGuardProgramsDirectory
    }

    AfterAll {
        # Full restore: re-register production tasks and start Guardian/Tray for the machine.
        Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$true
    }

    It 'silent install then silent uninstall exits 0' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -TaskSnapshot $script:TaskSnapshot
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
            Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$false
        }
    }

    It 'silent uninstall keeps config and log by default' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        $configPath = Join-Path $installRoot 'SmartGuard.config.json'
        $logPath = Join-Path $installRoot 'SmartGuard.log'
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -TaskSnapshot $script:TaskSnapshot
            $install.ExitCode | Should -Be 0

            Stop-SmartGuardForInstallerTest
            [System.IO.File]::WriteAllText($configPath, '{"keep":true}', [System.Text.UTF8Encoding]::new($false))
            [System.IO.File]::WriteAllText($logPath, 'keep-log', [System.Text.UTF8Encoding]::new($false))

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0 -Because "uninstall log: $($uninstall.LogPath)"

            Test-Path -LiteralPath $configPath | Should -Be $true
            Test-Path -LiteralPath $logPath | Should -Be $true
            Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | Should -Match 'keep'
            Get-Content -LiteralPath $logPath -Raw -Encoding UTF8 | Should -Match 'keep-log'
        }
        finally {
            Stop-SmartGuardForInstallerTest
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
            Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$false
        }
    }

    It 'install registers guardian task with --root at install path' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -SkipScheduledTasks:$false -TaskSnapshot $script:TaskSnapshot
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
            Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$false
        }
    }

    It 'install payload does not ship Register ps1 scripts' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -TaskSnapshot $script:TaskSnapshot
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
            Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$false
        }
    }

    It 'install registers tray task with --root at install path' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -SkipScheduledTasks:$false -TaskSnapshot $script:TaskSnapshot
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
            Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$false
        }
    }

    It 'upgrade install preserves user data files in install root' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        $markerPath = Join-Path $installRoot '.sg-upgrade-marker'
        try {
            $first = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -TaskSnapshot $script:TaskSnapshot
            $first.ExitCode | Should -Be 0

            $configPath = Join-Path $installRoot 'SmartGuard.config.json'
            # Skip-isolated silent install does not start Engine, so config may be absent until we seed user data.
            if (-not (Test-Path -LiteralPath $configPath)) {
                [System.IO.File]::WriteAllText(
                    $configPath,
                    '{"ActivePlanGUID":"8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c","LogFile":""}',
                    [System.Text.UTF8Encoding]::new($false))
            }
            Test-Path -LiteralPath $configPath | Should -Be $true

            'keep-me-on-upgrade' | Set-Content -LiteralPath $markerPath -Encoding UTF8
            Stop-SmartGuardForInstallerTest

            $second = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -PreserveExisting -TaskSnapshot $script:TaskSnapshot
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
            Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$false
        }
    }

    It 'install creates exactly one start menu shortcut and no dedicated settings or logs shortcut' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -SkipScheduledTasks:$false -TaskSnapshot $script:TaskSnapshot
            $install.ExitCode | Should -Be 0

            ($script:ProgramsDir | Get-ChildItem -Filter 'SmartGuard*.lnk' -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty Name |
                Sort-Object) | Should -Be @('SmartGuard.lnk')

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0
        }
        finally {
            Stop-SmartGuardForInstallerTest
            foreach ($path in Get-SmartGuardProgramsShortcuts) {
                Remove-Item -LiteralPath $path.FullName -Force -ErrorAction SilentlyContinue
            }
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
            Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$false
        }
    }

    It 'repair removes legacy tray settings and logs shortcuts before writing the canonical shortcut' {
        $installRoot = Join-Path $env:TEMP ('sg-installer-ui-' + [Guid]::NewGuid().ToString('N'))
        $trayShortcutName = "SmartGuard $([char]0x6258)$([char]0x76D8).lnk"
        $settingsShortcutName = "SmartGuard $([char]0x8BBE)$([char]0x7F6E).lnk"
        $logsShortcutName = "SmartGuard $([char]0x65E5)$([char]0x5FD7).lnk"
        try {
            $install = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -TaskSnapshot $script:TaskSnapshot
            $install.ExitCode | Should -Be 0

            foreach ($name in @($trayShortcutName, $settingsShortcutName, $logsShortcutName)) {
                New-ShortcutFixture -Directory $script:ProgramsDir -Name $name
            }

            $repair = Invoke-SmartGuardRepair -SetupExe $script:SetupExe -InstallRoot $installRoot -SkipScheduledTasks:$false -TaskSnapshot $script:TaskSnapshot
            $repair.ExitCode | Should -Be 0

            (Get-SmartGuardProgramsShortcuts | Select-Object -ExpandProperty Name) | Should -Be @('SmartGuard.lnk')

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0
        }
        finally {
            Stop-SmartGuardForInstallerTest
            foreach ($path in Get-SmartGuardProgramsShortcuts) {
                Remove-Item -LiteralPath $path.FullName -Force -ErrorAction SilentlyContinue
            }
            if (Test-Path -LiteralPath $installRoot) {
                Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
            Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$false
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
        $canonicalShortcut = Join-Path $programsDir 'SmartGuard.lnk'
        $legacyTrayShortcut = Join-Path $programsDir $trayShortcutName
        $legacySettingsShortcut = Join-Path $programsDir $settingsShortcutName
        $legacyLogsShortcut = Join-Path $programsDir $logsShortcutName
        $legacyGroupShortcuts = @(
            (Join-Path $legacyGroupDir $trayShortcutName),
            (Join-Path $legacyGroupDir $settingsShortcutName),
            (Join-Path $legacyGroupDir $logsShortcutName),
            (Join-Path $legacyGroupDir $legacyUninstallShortcutName)
        )
        try {
            $first = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -TaskSnapshot $script:TaskSnapshot
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

            foreach ($path in @($legacyTrayShortcut, $legacySettingsShortcut, $legacyLogsShortcut) + $legacyGroupShortcuts) {
                $directory = Split-Path -Parent $path
                if (-not [string]::IsNullOrWhiteSpace($directory)) {
                    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
                }
                [System.IO.File]::WriteAllBytes($path, [byte[]]@())
            }

            Remove-Item -LiteralPath $canonicalShortcut -Force -ErrorAction SilentlyContinue
            Stop-SmartGuardForInstallerTest

            $repair = Invoke-SmartGuardSilentInstall -SetupExe $script:SetupExe -InstallRoot $installRoot -PreserveExisting -SkipScheduledTasks:$false -TaskSnapshot $script:TaskSnapshot
            $repair.ExitCode | Should -Be 0

            Test-Path -LiteralPath $legacyLib | Should -Be $false
            Test-Path -LiteralPath (Join-Path $legacyBin 'SmartGuard.LogViewer.exe') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $legacyBin 'SmartGuard.LogViewer.dll') | Should -Be $false
            foreach ($path in $legacyGroupShortcuts) {
                Wait-SmartGuardPath -Path $path -ShouldExist $false | Should -Be $true -Because $path
            }
            foreach ($path in @($legacyTrayShortcut, $legacySettingsShortcut, $legacyLogsShortcut)) {
                Wait-SmartGuardPath -Path $path -ShouldExist $false | Should -Be $true -Because $path
            }
            Wait-SmartGuardPath -Path $canonicalShortcut -ShouldExist $true | Should -Be $true

            $uninstall = Invoke-SmartGuardSilentUninstall -InstallRoot $installRoot
            $uninstall.ExitCode | Should -Be 0
        }
        finally {
            Stop-SmartGuardForInstallerTest
            foreach ($path in @($canonicalShortcut, $legacyTrayShortcut, $legacySettingsShortcut, $legacyLogsShortcut)) {
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
            Restore-SmartGuardScheduledTasks -Snapshot $script:TaskSnapshot -StartProcesses:$false
        }
    }
}
