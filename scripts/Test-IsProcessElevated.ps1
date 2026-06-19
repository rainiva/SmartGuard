function Test-IsProcessElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ElevationDeclinedMarkerPath {
    param([string]$RepoRoot)
    return Join-Path $RepoRoot '.SmartGuard.elevation-declined'
}

function Test-RunTestsNeedsInstallerElevation {
    param([string]$RepoRoot)

    if ($env:SMARTGUARD_SKIP_INSTALLER_TESTS -eq '1') {
        return $false
    }

    if ($env:SMARTGUARD_ELEVATED_RUN -eq '1') {
        return $false
    }

    if (Test-IsProcessElevated) {
        return $false
    }

    $marker = Get-ElevationDeclinedMarkerPath -RepoRoot $RepoRoot
    if (Test-Path -LiteralPath $marker) {
        return $false
    }

    return Test-Path -LiteralPath (Join-Path $RepoRoot 'Tests\Integration\InstallerUserFlow.Tests.ps1')
}

function Invoke-RunTestsSingleElevation {
    param([string]$RunTestsScript)

    $repoRoot = Split-Path -Parent $RunTestsScript
    $marker = Get-ElevationDeclinedMarkerPath -RepoRoot $repoRoot

    Write-Host 'SmartGuard: requesting one-time UAC elevation for the full test suite...' -ForegroundColor Cyan
    $escaped = $RunTestsScript.Replace("'", "''")
    $command = "& { `$env:SMARTGUARD_ELEVATED_RUN='1'; & '$escaped' }"
    $proc = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-Command',
        $command
    )

    if ($null -eq $proc) {
        Write-Host 'UAC elevation was cancelled; skipping installer tests this session.' -ForegroundColor Yellow
        try {
            [void](New-Item -ItemType File -Path $marker -Force -ErrorAction SilentlyContinue)
        }
        catch {
            # marker creation is best-effort
        }
        return
    }

    if ($proc.ExitCode -ne 0) {
        exit $proc.ExitCode
    }
}
