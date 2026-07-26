#Requires -Version 5.1
<#
  Shared stop helper for integration tests and dev scripts.
  Delegates to SmartGuard.Engine.exe --uninstall when elevated; otherwise kills processes only (no UAC).
#>

$script:SmartGuardStopRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $script:SmartGuardStopRepoRoot 'scripts\Test-IsProcessElevated.ps1')

function Wait-SmartGuardEngineExit {
    param(
        [int]$ProcessId = 0,
        [int]$TimeoutMs = 2000,
        [int]$PollMs = 100
    )

    $deadline = [datetime]::UtcNow.AddMilliseconds($TimeoutMs)
    while ([datetime]::UtcNow -lt $deadline) {
        if ($ProcessId -gt 0) {
            $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
            if (-not $proc) {
                return $true
            }
        }
        else {
            $running = Get-Process -Name 'SmartGuard.Engine' -ErrorAction SilentlyContinue
            if (-not $running) {
                return $true
            }
        }

        Start-Sleep -Milliseconds $PollMs
    }

    return $false
}

function Get-SmartGuardProcessesForStop {
    param([int]$ProcessId = 0)

    $byId = @{}
    if ($ProcessId -gt 0) {
        $target = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($target) {
            $byId[$target.Id] = $target
        }
    }

    foreach ($process in @(Get-Process -Name 'SmartGuard.Engine', 'SmartGuard.Tray' -ErrorAction SilentlyContinue)) {
        $byId[$process.Id] = $process
    }

    return @($byId.Values)
}

function Assert-SmartGuardProcessesExited {
    param(
        [int]$ProcessId = 0,
        [int]$TimeoutMs = 2000,
        [int]$PollMs = 100,
        [object[]]$StopFailures = @()
    )

    $effectiveTimeoutMs = [Math]::Min([Math]::Max($TimeoutMs, 0), 2000)
    $deadline = [DateTime]::UtcNow.AddMilliseconds($effectiveTimeoutMs)
    $remaining = @()

    do {
        $remaining = @(Get-SmartGuardProcessesForStop -ProcessId $ProcessId)
        if ($remaining.Count -eq 0) {
            return
        }

        $remainingMs = [int][Math]::Ceiling(($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        if ($remainingMs -le 0) {
            break
        }

        Start-Sleep -Milliseconds ([Math]::Min([Math]::Max($PollMs, 1), $remainingMs))
    } while ($true)

    $processDetails = @($remaining | Sort-Object Id | ForEach-Object {
        "PID=$($_.Id), Name=$($_.ProcessName)"
    }) -join '; '
    $failureDetails = @($StopFailures | ForEach-Object {
        "PID=$($_.Id), Name=$($_.Name), Error=$($_.Error)"
    }) -join '; '
    $elevated = Test-IsProcessElevated
    $failureSuffix = if ($failureDetails) { " StopFailures=$failureDetails." } else { '' }

    throw "SmartGuard processes did not exit within $effectiveTimeoutMs ms: $processDetails. CurrentProcessElevated=$elevated.$failureSuffix Re-run the complete test gate from an administrator PowerShell, then investigate duplicate single-instance or startup failures."
}

function Stop-SmartGuardProcessesWithoutElevation {
    param(
        [int]$ProcessId = 0,
        [int]$TimeoutMs = 2000
    )

    $stopFailures = @()
    foreach ($process in @(Get-SmartGuardProcessesForStop -ProcessId $ProcessId)) {
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch {
            $stopFailures += [pscustomobject]@{
                Id = $process.Id
                Name = $process.ProcessName
                Error = $_.Exception.Message
            }
        }
    }

    Assert-SmartGuardProcessesExited -ProcessId $ProcessId -TimeoutMs $TimeoutMs -StopFailures $stopFailures
}

function Stop-SmartGuardProcesses {
    param(
        [string]$EngineExe,
        [string]$Root,
        [int]$ProcessId = 0
    )

    if (-not (Test-IsProcessElevated)) {
        Stop-SmartGuardProcessesWithoutElevation -ProcessId $ProcessId
        return
    }

    if ($EngineExe -and (Test-Path -LiteralPath $EngineExe)) {
        # Always kill live processes first. Engine --uninstall can race with a still-running
        # Guardian/Tray (and Start-Process -Wait callers appear hung when uninstall stalls).
        Stop-SmartGuardProcessesWithoutElevation -ProcessId $ProcessId

        $arguments = @()
        if ($Root) {
            $arguments += @('--root', $Root)
        }
        $arguments += '--uninstall'

        $stdoutPath = [System.IO.Path]::GetTempFileName()
        $stderrPath = [System.IO.Path]::GetTempFileName()
        try {
            $proc = Start-Process -FilePath $EngineExe `
                -ArgumentList $arguments `
                -PassThru `
                -NoNewWindow `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath
            if (-not $proc.WaitForExit(120000)) {
                try { $proc.Kill() } catch { /* ignore */ }
                throw "SmartGuard Engine --uninstall timed out after 120s (exe='$EngineExe')."
            }
        }
        finally {
            Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
        }

        Wait-SmartGuardEngineExit -ProcessId $ProcessId | Out-Null
        return
    }

    throw "SmartGuard.Engine.exe not found at '$EngineExe'. Run build.cmd before stopping SmartGuard."
}

function Resolve-SmartGuardRepoRoot {
    param([string]$ScriptRoot = $PSScriptRoot)
    if ($global:SG_TestRepoRoot) { return $global:SG_TestRepoRoot }
    if ($global:SG_UI_RepoRoot) { return $global:SG_UI_RepoRoot }
    return (Resolve-Path (Join-Path $ScriptRoot '..\..')).Path
}

function Stop-SmartGuardForIntegrationTest {
    param([string]$RepoRoot)
    $root = if ($RepoRoot) { $RepoRoot } else { Resolve-SmartGuardRepoRoot }
    $engineExe = Join-Path $root 'bin\SmartGuard.Engine.exe'
    Stop-SmartGuardProcesses -EngineExe $engineExe -Root $root
}
