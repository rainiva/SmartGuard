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
        throw "Missing publish output: $global:SG_UI_EngineExe (run build.cmd)"
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
    $engineExe = $global:SG_UI_EngineExe
    $needsBuild = (-not $setup) `
        -or ((Get-Item -LiteralPath $iss).LastWriteTime -gt $setup.LastWriteTime) `
        -or ((Test-Path -LiteralPath $engineExe) -and (Get-Item -LiteralPath $engineExe).LastWriteTime -gt $setup.LastWriteTime)

    if ($needsBuild) {
        if (-not (Test-Path -LiteralPath $global:SG_UI_IsccPath)) {
            throw "ISCC not found: $($global:SG_UI_IsccPath)"
        }
        $build = Join-Path $global:SG_UI_RepoRoot 'src\SmartGuard.Packaging\SmartGuard.Packaging.csproj'
        if (-not (Test-Path -LiteralPath $build)) {
            throw "Missing packaging project: $build"
        }
        & dotnet run --project $build --no-launch-profile -- installer `
            --root $global:SG_UI_RepoRoot `
            --configuration Release `
            --skip-version-bump `
            --iscc $global:SG_UI_IsccPath | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "SmartGuard.Packaging installer failed with exit code $LASTEXITCODE"
        }
    }

    return (Get-InstallerSetupExe)
}

. (Join-Path $PSScriptRoot 'SmartGuardStop.ps1')

function Stop-SmartGuardForInstallerTest {
    Stop-SmartGuardForIntegrationTest
}

function Invoke-SmartGuardSilentInstall {
    param(
        [string]$SetupExe,
        [string]$InstallRoot,
        [switch]$PreserveExisting
    )

    if (-not $PreserveExisting.IsPresent) {
        if (Test-Path -LiteralPath $InstallRoot) {
            Remove-Item -LiteralPath $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    }
    elseif (-not (Test-Path -LiteralPath $InstallRoot)) {
        New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    }

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

function Get-SmartGuardProgramsDirectory {
    Join-Path ([Environment]::GetFolderPath([System.Environment+SpecialFolder]::ApplicationData)) 'Microsoft\Windows\Start Menu\Programs'
}

function Get-SmartGuardProgramsShortcuts {
    Get-ChildItem -LiteralPath (Get-SmartGuardProgramsDirectory) -Filter 'SmartGuard*.lnk' -ErrorAction SilentlyContinue |
        Sort-Object Name
}

function New-ShortcutFixture {
    param(
        [string]$Directory,
        [string]$Name
    )

    if (-not [string]::IsNullOrWhiteSpace($Directory)) {
        [System.IO.Directory]::CreateDirectory($Directory) | Out-Null
    }

    [System.IO.File]::WriteAllBytes((Join-Path $Directory $Name), [byte[]]@())
}

function Invoke-SmartGuardRepair {
    param(
        [string]$SetupExe,
        [string]$InstallRoot
    )

    Stop-SmartGuardForInstallerTest
    return Invoke-SmartGuardSilentInstall -SetupExe $SetupExe -InstallRoot $InstallRoot -PreserveExisting
}
