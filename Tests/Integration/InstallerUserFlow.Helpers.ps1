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
. (Join-Path $PSScriptRoot 'Restore-SmartGuardScheduledTasks.ps1')
. (Join-Path $PSScriptRoot 'ReadOnlyEngineRoot.ps1')

function Stop-SmartGuardForInstallerTest {
    Stop-SmartGuardForIntegrationTest
}

function Invoke-SmartGuardSilentInstall {
    param(
        [string]$SetupExe,
        [string]$InstallRoot,
        [switch]$PreserveExisting,
        [bool]$SkipScheduledTasks = $true,
        $TaskSnapshot = $null
    )

    if (-not $PreserveExisting.IsPresent) {
        if (Test-Path -LiteralPath $InstallRoot) {
            Remove-Item -LiteralPath $InstallRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
        # Setup ends with "schtasks /Run" for the guardian task; when a test registers that task for this
        # root, a real Engine starts here. Seed the root so it only reads power settings.
        Initialize-SmartGuardReadOnlyEngineRoot -Root $InstallRoot
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

    # Prior It's finally Restore may have re-registered production tasks. Disable/end them and
    # kill processes so Inno PrepareToInstall does not call Engine --uninstall while Guardian is live
    # (schtasks /End can stall and freeze Setup forever).
    Stop-SmartGuardForInstallerTest
    foreach ($taskName in @('SmartGuard Guardian', 'SmartGuard Tray')) {
        # /End has no /F; swallow missing-task noise (tasks often already deleted by Stop).
        cmd.exe /c "schtasks.exe /End /TN `"$taskName`" 2>nul" | Out-Null
        cmd.exe /c "schtasks.exe /Change /TN `"$taskName`" /Disable 2>nul" | Out-Null
    }
    Stop-SmartGuardProcessesWithoutElevation
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        $alive = Get-Process -Name 'SmartGuard.Engine', 'SmartGuard.Tray', 'SmartGuard.Settings' -ErrorAction SilentlyContinue
        if (-not $alive) { break }
        $alive | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 200
    }

    $prev = $env:SMARTGUARD_SKIP_SCHEDULED_TASKS
    try {
        if ($SkipScheduledTasks) {
            $env:SMARTGUARD_SKIP_SCHEDULED_TASKS = '1'
        }
        else {
            Remove-Item Env:SMARTGUARD_SKIP_SCHEDULED_TASKS -ErrorAction SilentlyContinue
        }

        $proc = Start-Process -FilePath $SetupExe -ArgumentList $args -PassThru
        if (-not $proc.WaitForExit(300000)) {
            try { $proc.Kill() } catch { /* ignore */ }
            throw "Silent install timed out after 300s for root '$InstallRoot'. log=$log"
        }
        $result = [PSCustomObject]@{
            ExitCode = $proc.ExitCode
            LogPath  = $log
            Root     = $InstallRoot
        }

        # Only assert skip isolation when install actually completed (Engine --install ran).
        if ($SkipScheduledTasks -and $result.ExitCode -eq 0) {
            Assert-SmartGuardSkipInstallHonored -InstallRoot $InstallRoot -Snapshot $TaskSnapshot
        }

        return $result
    }
    finally {
        if ($null -eq $prev) {
            Remove-Item Env:SMARTGUARD_SKIP_SCHEDULED_TASKS -ErrorAction SilentlyContinue
        }
        else {
            $env:SMARTGUARD_SKIP_SCHEDULED_TASKS = $prev
        }
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
        [string]$InstallRoot,
        [bool]$SkipScheduledTasks = $true,
        $TaskSnapshot = $null
    )

    Stop-SmartGuardForInstallerTest
    return Invoke-SmartGuardSilentInstall -SetupExe $SetupExe -InstallRoot $InstallRoot -PreserveExisting -SkipScheduledTasks:$SkipScheduledTasks -TaskSnapshot $TaskSnapshot
}
