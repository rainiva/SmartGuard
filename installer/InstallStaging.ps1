# InstallStaging.ps1 — staging layout helpers (Phase 5 installer)

function Get-InstallerRequiredRelativePaths {
    @(
        'bin\SmartGuard.Engine.exe'
        'bin\SmartGuard.Tray.exe'
        'bin\SmartGuard.LogViewer.exe'
        'bin\SmartGuard.Settings.exe'
        'lib\SmartGuard.ico'
        'lib\SmartGuard.Settings.xaml'
        'Register-SmartGuardTask.ps1'
        'Register-TrayTask.ps1'
        'license_zh-CN.txt'
        'VERSION.txt'
    )
}

function Get-InstallerRequiredRedistPattern {
    'redist\windowsdesktop-runtime-*-win-x64.exe'
}

function New-InstallerFakeStaging {
    param([Parameter(Mandatory)][string]$StagingDir)

    $paths = Get-InstallerRequiredRelativePaths
    foreach ($rel in $paths) {
        $full = Join-Path $StagingDir $rel
        $parent = Split-Path -Parent $full
        if ($parent -and -not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        if ($rel -match '\.(exe|ico|xaml|ps1|txt)$') {
            Set-Content -LiteralPath $full -Value 'placeholder' -Encoding UTF8
        }
    }

    $redistDir = Join-Path $StagingDir 'redist'
    New-Item -ItemType Directory -Path $redistDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $redistDir 'windowsdesktop-runtime-8.0.18-win-x64.exe') -Value 'placeholder' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $redistDir 'runtime-installer.txt') -Value 'windowsdesktop-runtime-8.0.18-win-x64.exe' -Encoding ASCII
}

function Test-InstallerStagingLayout {
    param(
        [Parameter(Mandatory)][string]$StagingDir,
        [switch]$RequireRedist
    )

    if (-not (Test-Path -LiteralPath $StagingDir)) {
        throw "Staging directory not found: $StagingDir"
    }

    $missing = @()
    foreach ($rel in (Get-InstallerRequiredRelativePaths)) {
        $full = Join-Path $StagingDir $rel
        if (-not (Test-Path -LiteralPath $full)) {
            $missing += $rel
        }
    }

    if ($RequireRedist.IsPresent) {
        $redist = Join-Path $StagingDir 'redist'
        $runtimeList = @(Get-ChildItem -LiteralPath $redist -Filter 'windowsdesktop-runtime-*-win-x64.exe' -ErrorAction SilentlyContinue)
        if ($runtimeList.Count -lt 1) {
            $missing += (Get-InstallerRequiredRedistPattern)
        }
        $marker = Join-Path $redist 'runtime-installer.txt'
        if (-not (Test-Path -LiteralPath $marker)) {
            $missing += 'redist\runtime-installer.txt'
        }
    }

    if ($missing.Count -gt 0) {
        throw ('Installer staging incomplete. Missing: ' + ($missing -join ', '))
    }
}
