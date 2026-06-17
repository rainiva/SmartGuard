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
}
