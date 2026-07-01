. (Join-Path $PSScriptRoot 'SmartGuardStop.ps1')

function Stop-SmartGuardForTrayCoreTest {
    Stop-SmartGuardForIntegrationTest
}

function Initialize-TrayCoreUserFlowContext {
    param([string]$RepoRoot)
    $global:SG_TestRepoRoot = $RepoRoot
    $global:SG_TestEngineBin = Join-Path $RepoRoot 'bin'
    $global:SG_TestEngineExe = Join-Path $global:SG_TestEngineBin 'SmartGuard.Engine.exe'
    if (-not (Test-Path -LiteralPath $global:SG_TestEngineExe)) {
        throw "Missing publish output: $global:SG_TestEngineExe (run scripts\Publish-All.ps1)"
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
        [int]$TimeoutSec = 25
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $StatusPath) {
            return Get-Content -LiteralPath $StatusPath -Raw | ConvertFrom-Json
        }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

function Stop-EngineTree {
    param([int]$ProcessId)
    if ($ProcessId -gt 0) {
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    }
    Get-Process -Name 'SmartGuard.Engine' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}
