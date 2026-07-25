function Test-IsProcessElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ElevationDeclinedMarkerPath {
    param([string]$RepoRoot)
    return Join-Path $RepoRoot '.SmartGuard.elevation-declined'
}

function Get-WindowsPowerShellExePath {
    $command = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    return Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
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
    $runner = Join-Path $RepoRoot 'scripts\Invoke-ElevatedInstallerUserFlow.ps1'
    $transcript = Join-Path $RepoRoot '.work\test-results\installer-user-flow-elevated.transcript.log'
    $resultPath = Join-Path $RepoRoot '.work\test-results\installer-user-flow-elevated.result.json'
    Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
    $powershellExe = Get-WindowsPowerShellExePath
    try {
        $proc = Start-Process -FilePath $powershellExe -Verb RunAs -PassThru -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', $runner,
            '-RepoRoot', $RepoRoot,
            '-TestScriptPath', $TestScriptPath,
            '-DeclinedMarkerPath', $marker,
            '-TranscriptPath', $transcript,
            '-ResultPath', $resultPath
        )
    }
    catch {
        throw "Unable to launch elevated Windows PowerShell ($powershellExe): $($_.Exception.Message)"
    }

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

    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $resultPath) {
            try {
                $result = [System.IO.File]::ReadAllText($resultPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
                return [PSCustomObject]@{
                    HandedOff  = $true
                    ExitCode   = [int]$result.ExitCode
                    ShouldSkip = $false
                    SkipReason = $null
                }
            }
            catch {
                # The elevated writer may still be atomically finishing its result file.
            }
        }

        Start-Sleep -Milliseconds 250
    }

    return [PSCustomObject]@{
        HandedOff  = $true
        ExitCode   = 1
        ShouldSkip = $false
        SkipReason = $null
    }
}
