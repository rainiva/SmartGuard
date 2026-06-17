function Initialize-InstallerUserFlowContext {
    param(
        [string]$RepoRoot,
        [string]$IsccPath = 'D:\Apps\Inno Setup 6\ISCC.exe'
    )

    $global:SG_UI_RepoRoot = $RepoRoot
    $global:SG_UI_DistDir = Join-Path $RepoRoot 'dist'
    $global:SG_UI_IsccPath = $IsccPath
    $global:SG_UI_EngineBin = Join-Path $RepoRoot 'bin'
    $global:SG_UI_EngineExe = Join-Path $global:SG_UI_EngineBin 'SmartGuard.Engine.exe'

    if (-not (Test-Path -LiteralPath $global:SG_UI_EngineExe)) {
        throw "Missing publish output: $global:SG_UI_EngineExe (run scripts\Publish-All.ps1)"
    }
}

function Get-InstallerSetupExe {
    $setups = Get-ChildItem -LiteralPath $global:SG_UI_DistDir -Filter 'SmartGuard-Setup-*-x64.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
    if (-not $setups) {
        throw "Missing installer in $($global:SG_UI_DistDir). Build installer first."
    }
    return $setups[0].FullName
}

function Ensure-InstallerBuilt {
    $setup = Get-ChildItem -LiteralPath $global:SG_UI_DistDir -Filter 'SmartGuard-Setup-*-x64.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    $iss = Join-Path $global:SG_UI_RepoRoot 'installer\SmartGuard.iss'
    $needsBuild = (-not $setup) -or ((Get-Item -LiteralPath $iss).LastWriteTime -gt $setup.LastWriteTime)

    if ($needsBuild) {
        if (-not (Test-Path -LiteralPath $global:SG_UI_IsccPath)) {
            throw "ISCC not found: $($global:SG_UI_IsccPath)"
        }
        $build = Join-Path $global:SG_UI_RepoRoot 'installer\Build-Installer.ps1'
        & powershell -NoProfile -ExecutionPolicy Bypass -File $build `
            -SkipVersionBump `
            -IsccPath $global:SG_UI_IsccPath | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Build-Installer.ps1 failed with exit code $LASTEXITCODE"
        }
    }

    return (Get-InstallerSetupExe)
}

function Stop-SmartGuardForInstallerTest {
    schtasks /End /TN 'SmartGuard Guardian' 2>$null | Out-Null
    schtasks /End /TN 'SmartGuard Tray' 2>$null | Out-Null
    Get-Process -Name 'SmartGuard.Tray', 'SmartGuard.Engine', 'SmartGuard.LogViewer', 'SmartGuard.Settings' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

function Invoke-SmartGuardSilentInstall {
    param(
        [string]$SetupExe,
        [string]$InstallRoot
    )

    if (Test-Path -LiteralPath $InstallRoot) {
        Remove-Item -LiteralPath $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null

    $log = Join-Path $InstallRoot 'install-ui-test.log'
    $args = @(
        "/DIR=$InstallRoot",
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/NOCANCEL',
        '/SP-',
        '/CLOSEAPPLICATIONS=off',
        "/LOG=$log"
    )

    $proc = Start-Process -FilePath $SetupExe -ArgumentList $args -PassThru -Wait
    return [PSCustomObject]@{
        ExitCode = $proc.ExitCode
        LogPath  = $log
        Root     = $InstallRoot
    }
}

function Invoke-SmartGuardSilentUninstall {
    param([string]$InstallRoot)

    $unins = Get-ChildItem -LiteralPath $InstallRoot -Filter 'unins*.exe' -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -First 1
    if (-not $unins) {
        throw "Missing uninstaller in $InstallRoot"
    }

    $log = Join-Path $InstallRoot 'uninstall-ui-test.log'
    $proc = Start-Process -FilePath $unins.FullName -ArgumentList @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/NOCANCEL',
        "/LOG=$log"
    ) -PassThru -Wait

    return [PSCustomObject]@{
        ExitCode = $proc.ExitCode
        LogPath  = $log
    }
}
