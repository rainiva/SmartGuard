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
    $command = "& { `$env:SMARTGUARD_ELEVATED_RUN='1'; & '$escaped'; exit `$LASTEXITCODE }"
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
        return [PSCustomObject]@{ HandedOff = $false; ExitCode = 0 }
    }

    return [PSCustomObject]@{ HandedOff = $true; ExitCode = $proc.ExitCode }
}

function Invoke-InstallerUserFlowSingleElevation {
    param(
        [string]$RepoRoot,
        [string]$TestScriptPath
    )

    if ($env:SMARTGUARD_SKIP_INSTALLER_TESTS -eq '1') {
        return [PSCustomObject]@{
            HandedOff  = $false
            ExitCode   = 0
            ShouldSkip = $true
            SkipReason = 'SMARTGUARD_SKIP_INSTALLER_TESTS=1'
        }
    }

    if (Test-IsProcessElevated) {
        return [PSCustomObject]@{
            HandedOff  = $false
            ExitCode   = 0
            ShouldSkip = $false
            SkipReason = $null
        }
    }

    if ($env:SMARTGUARD_ELEVATED_RUN -eq '1') {
        return [PSCustomObject]@{
            HandedOff  = $false
            ExitCode   = 0
            ShouldSkip = $true
            SkipReason = 'requires administrator (accept one UAC prompt or run elevated shell)'
        }
    }

    $marker = Get-ElevationDeclinedMarkerPath -RepoRoot $RepoRoot
    if (Test-Path -LiteralPath $marker) {
        return [PSCustomObject]@{
            HandedOff  = $false
            ExitCode   = 0
            ShouldSkip = $true
            SkipReason = 'requires administrator (accept one UAC prompt or run elevated shell)'
        }
    }

    Write-Host 'SmartGuard: requesting one-time UAC elevation for installer integration tests...' -ForegroundColor Cyan
    $escapedRepoRoot = $RepoRoot.Replace("'", "''")
    $escapedMarker = $marker.Replace("'", "''")
    $escapedTestPath = $TestScriptPath.Replace("'", "''")
    $command = @"
& {
    Set-Location '$escapedRepoRoot'
    `$env:SMARTGUARD_ELEVATED_RUN='1'
    Remove-Item -LiteralPath '$escapedMarker' -Force -ErrorAction SilentlyContinue
    Import-Module Pester -MinimumVersion 5.0 -Force
    `$result = Invoke-Pester -Path '$escapedTestPath' -PassThru
    if (`$null -eq `$result) { exit 1 }
    if (`$result.FailedCount -gt 0) { exit 1 }
    exit 0
}
"@
    $proc = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-Command',
        $command
    )

    if ($null -eq $proc) {
        Write-Host 'UAC elevation was cancelled; skipping installer integration this session.' -ForegroundColor Yellow
        try {
            [void](New-Item -ItemType File -Path $marker -Force -ErrorAction SilentlyContinue)
        }
        catch {
            # marker creation is best-effort
        }
        return [PSCustomObject]@{
            HandedOff  = $false
            ExitCode   = 0
            ShouldSkip = $true
            SkipReason = 'UAC elevation was cancelled; skipping installer integration this session.'
        }
    }

    return [PSCustomObject]@{
        HandedOff  = $true
        ExitCode   = $proc.ExitCode
        ShouldSkip = $false
        SkipReason = $null
    }
}
