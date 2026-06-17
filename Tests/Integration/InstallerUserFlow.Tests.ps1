#Requires -Version 5.1
<#
  真实用户流程：静默安装 -> 验证产物 -> 静默卸载（等价于向导点完安装/卸载，无自定义控件交互）。
  若卸载脚本 Access violation，本测试必须失败。
#>
Describe 'Installer user flow (integration)' {
    BeforeAll {
        . (Join-Path $PSScriptRoot 'InstallerUserFlow.Helpers.ps1')
        $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
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
}
