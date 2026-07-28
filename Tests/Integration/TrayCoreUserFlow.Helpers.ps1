. (Join-Path $PSScriptRoot 'SmartGuardStop.ps1')
. (Join-Path $PSScriptRoot '..\..\scripts\SmartGuardPathConstants.ps1')

function Stop-SmartGuardForTrayCoreTest {
    Stop-SmartGuardForIntegrationTest
}

function Initialize-TrayCoreUserFlowContext {
    param([string]$RepoRoot)
    $global:SG_TestRepoRoot = $RepoRoot
    $global:SG_TestEngineBin = Join-Path $RepoRoot 'bin'
    $global:SG_TestEngineExe = Join-Path $global:SG_TestEngineBin 'SmartGuard.Engine.exe'
    if (-not (Test-Path -LiteralPath $global:SG_TestEngineExe)) {
        throw "Missing publish output: $global:SG_TestEngineExe (run build.cmd)"
    }
}

function Copy-EnginePayload {
    param([string]$InstallRoot)
    $destBin = Join-Path $InstallRoot 'bin'
    New-Item -ItemType Directory -Path $destBin -Force | Out-Null
    Copy-Item -Path (Join-Path $global:SG_TestEngineBin '*') -Destination $destBin -Recurse -Force
}

function Wait-StatusFile {
    param(
        [string]$StatusPath,
        [object]$Process,
        [string]$StartupLogPath,
        [int]$TimeoutSec = 25
    )

    if (-not $StartupLogPath) {
        $StartupLogPath = Join-Path (Split-Path -Parent $StatusPath) $SmartGuardStartupLogFileName
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $statusReadError = $null
        if (Test-Path -LiteralPath $StatusPath) {
            try {
                return Get-Content -LiteralPath $StatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
            }
            catch {
                $statusReadError = $_.Exception.Message
            }
        }

        if ($Process -and $Process.HasExited) {
            $exitCode = try { $Process.ExitCode } catch { 'unavailable' }
            $startupLogTail = '(startup log unavailable)'
            if (Test-Path -LiteralPath $StartupLogPath) {
                try {
                    $startupLogTail = @(
                        [System.IO.File]::ReadAllLines(
                            $StartupLogPath,
                            [System.Text.UTF8Encoding]::new($false)) |
                            Select-Object -Last 20
                    ) -join [Environment]::NewLine
                }
                catch {
                    $startupLogTail = "(unable to read startup log: $($_.Exception.Message))"
                }
            }

            $statusSuffix = if ($statusReadError) { " StatusReadError=$statusReadError." } else { '' }
            throw "Engine process exited before status publication. PID=$($Process.Id); ExitCode=$exitCode; StatusPath='$StatusPath'; StartupLogTail=$startupLogTail.$statusSuffix A duplicate single instance or startup failure prevented status publication."
        }
        Start-Sleep -Milliseconds 400
    }

    # Timeout with the engine still alive: never return $null silently —
    # the caller's assertion would fail with zero evidence. Attach the same
    # startup-log diagnostics as the exited-engine path.
    $timeoutLogTail = '(startup log unavailable)'
    if (Test-Path -LiteralPath $StartupLogPath) {
        try {
            $timeoutLogTail = @(
                [System.IO.File]::ReadAllLines(
                    $StartupLogPath,
                    [System.Text.UTF8Encoding]::new($false)) |
                    Select-Object -Last 20
            ) -join [Environment]::NewLine
        }
        catch {
            $timeoutLogTail = "(unable to read startup log: $($_.Exception.Message))"
        }
    }
    $timeoutPid = if ($Process) { $Process.Id } else { 'unknown' }
    throw "Timed out after $TimeoutSec s waiting for status publication while the engine is still running. PID=$timeoutPid; StatusPath='$StatusPath'; StartupLogTail=$timeoutLogTail."
}

function Stop-EngineTree {
    param(
        [int]$ProcessId = 0,
        [string]$InstallRoot
    )

    if ($InstallRoot) {
        $engineExe = Join-Path $InstallRoot 'bin\SmartGuard.Engine.exe'
        if (Test-Path -LiteralPath $engineExe) {
            Stop-SmartGuardProcesses -EngineExe $engineExe -Root $InstallRoot -ProcessId $ProcessId
            return
        }
    }

    Stop-SmartGuardForIntegrationTest
}
