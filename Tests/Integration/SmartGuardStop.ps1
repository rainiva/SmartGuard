#Requires -Version 5.1
<#
  Shared stop helper for integration tests and dev scripts.
  Delegates to SmartGuard.Engine.exe --uninstall when the engine binary exists.
#>

function Stop-SmartGuardProcesses {
    param(
        [string]$EngineExe,
        [string]$Root
    )

    if ($EngineExe -and (Test-Path -LiteralPath $EngineExe)) {
        $arguments = @()
        if ($Root) {
            $arguments += @('--root', $Root)
        }
        $arguments += '--uninstall'
        & $EngineExe @arguments 2>$null | Out-Null
        Start-Sleep -Seconds 2
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
