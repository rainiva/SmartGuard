#Requires -Version 5.1
<#
.SYNOPSIS
  Remove local SmartGuard build artifacts, installer staging, and diagnostic clutter.

.EXAMPLE
  ./scripts/Clean-Workspace.ps1 -DryRun -AllLocal
.EXAMPLE
  ./scripts/Clean-Workspace.ps1 -AllLocal
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$BuildArtifacts,
    [switch]$Staging,
    [switch]$Dist,
    [switch]$Logs,
    [switch]$Codegraph,
    [switch]$AllLocal,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    param([string]$ScriptRoot = $PSScriptRoot)
    return (Resolve-Path (Join-Path $ScriptRoot '..')).Path
}

function Get-DirectorySizeBytes {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    $sum = Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum
    if ($null -eq $sum -or $null -eq $sum.Sum) { return 0 }
    return [int64]$sum.Sum
}

function Format-Size {
    param([int64]$Bytes)
    if ($Bytes -ge 1GB) { return '{0:N2} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N2} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N2} KB' -f ($Bytes / 1KB) }
    return '{0} B' -f $Bytes
}

function Write-PlannedAction {
    param(
        [string]$Label,
        [string]$Path,
        [int64]$SizeBytes = 0
    )
    $sizeText = if ($SizeBytes -gt 0) { " ({0})" -f (Format-Size $SizeBytes) } else { '' }
    Write-Host "[$(if ($DryRun) { 'dry-run' } else { 'clean' })] $Label`: $Path$sizeText"
}

function Invoke-RemovePath {
    param(
        [string]$Label,
        [string]$Path
    )
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }

    $size = Get-DirectorySizeBytes $Path
    Write-PlannedAction -Label $Label -Path $Path -SizeBytes $size
    if ($DryRun) { return $size }

    if ($PSCmdlet.ShouldProcess($Path, "Remove $Label")) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Could not remove ${Path}: $($_.Exception.Message)"
            return 0
        }
    }
    return $size
}

function Invoke-RemoveFiles {
    param(
        [string]$Label,
        [string[]]$Paths
    )
    $total = [int64]0
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $item = Get-Item -LiteralPath $path -Force
        $size = if ($item.PSIsContainer) { Get-DirectorySizeBytes $path } else { [int64]$item.Length }
        Write-PlannedAction -Label $Label -Path $path -SizeBytes $size
        if (-not $DryRun -and $PSCmdlet.ShouldProcess($path, "Remove $Label")) {
            try {
                Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            }
            catch {
                Write-Warning "Could not remove ${path}: $($_.Exception.Message)"
                continue
            }
        }
        $total += $size
    }
    return $total
}

function Test-SmartGuardProcessRunning {
    $names = @('SmartGuard.Engine', 'SmartGuard.Tray', 'SmartGuard.Settings', 'SmartGuard.LogViewer')
    foreach ($name in $names) {
        if (Get-Process -Name $name -ErrorAction SilentlyContinue) { return $true }
    }
    return $false
}

function Invoke-DotNetClean {
    param(
        [string]$RepoRoot,
        [string]$Target,
        [string[]]$Configurations = @('Debug', 'Release')
    )
    foreach ($cfg in $Configurations) {
        $label = "dotnet clean $Target ($cfg)"
        Write-PlannedAction -Label $label -Path $Target
        if ($DryRun) { continue }
        if (-not $PSCmdlet.ShouldProcess($Target, $label)) { continue }

        & dotnet clean $Target -c $cfg --nologo -v q
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet clean failed for $Target ($cfg) with exit code $LASTEXITCODE"
        }
    }
}

function Invoke-DotNetCleanAllProjects {
    param(
        [string]$RepoRoot,
        [string[]]$Configurations = @('Debug', 'Release')
    )

    $projects = Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter *.csproj -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\\.worktrees\\' }

    foreach ($project in $projects) {
        Invoke-DotNetClean -RepoRoot $RepoRoot -Target $project.FullName -Configurations $Configurations
    }
}

$repoRoot = Get-RepoRoot
$anySwitch = $BuildArtifacts -or $Staging -or $Dist -or $Logs -or $Codegraph -or $AllLocal
if (-not $anySwitch) {
    $BuildArtifacts = $true
}

if ($AllLocal) {
    $BuildArtifacts = $true
    $Staging = $true
    $Dist = $true
    $Logs = $true
    $Codegraph = $true
}

$beforeBytes = Get-DirectorySizeBytes $repoRoot
Write-Host "SmartGuard workspace: $repoRoot"
Write-Host "Size before: $(Format-Size $beforeBytes)"
Write-Host ''

$freedBytes = [int64]0

if ($BuildArtifacts) {
    Invoke-DotNetCleanAllProjects -RepoRoot $repoRoot

    $artifactRoots = @(
        Join-Path $repoRoot 'src'
        Join-Path $repoRoot 'Tests'
    )
    foreach ($artifactRoot in $artifactRoots) {
        if (-not (Test-Path -LiteralPath $artifactRoot)) { continue }
        $artifactDirs = Get-ChildItem -LiteralPath $artifactRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @('bin', 'obj') }
        foreach ($dir in $artifactDirs) {
            $freedBytes += Invoke-RemovePath -Label 'build artifact' -Path $dir.FullName
        }
    }

    $rootBin = Join-Path $repoRoot 'bin'
    if (Test-Path -LiteralPath $rootBin) {
        if (Test-SmartGuardProcessRunning) {
            Write-Warning "SmartGuard process is running; skipping removal of $rootBin"
        }
        else {
            $freedBytes += Invoke-RemovePath -Label 'published bin' -Path $rootBin
        }
    }
}

if ($Staging) {
    $freedBytes += Invoke-RemovePath -Label 'installer staging' -Path (Join-Path $repoRoot 'installer\staging')
}

if ($Dist) {
    $freedBytes += Invoke-RemovePath -Label 'dist artifacts' -Path (Join-Path $repoRoot 'dist')
}

if ($Logs) {
    $logPatterns = @(
        'test-diag*.log'
        'test-diag.host.*.log'
        'test-run-output.*'
        'SmartGuard.log*'
        'test-result.txt'
        'settings-test.txt'
    )
    $logPaths = @()
    foreach ($pattern in $logPatterns) {
        $logPaths += Get-ChildItem -LiteralPath $repoRoot -Force -File -Filter $pattern -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty FullName
    }
    $freedBytes += Invoke-RemoveFiles -Label 'diagnostic log' -Paths $logPaths
}

if ($Codegraph) {
    $freedBytes += Invoke-RemovePath -Label 'codegraph index' -Path (Join-Path $repoRoot '.codegraph')
}

$afterBytes = if ($DryRun) { $beforeBytes } else { Get-DirectorySizeBytes $repoRoot }
Write-Host ''
Write-Host "Estimated reclaimable: $(Format-Size $freedBytes)"
if (-not $DryRun) {
    Write-Host "Size after: $(Format-Size $afterBytes)"
    Write-Host "Saved approximately: $(Format-Size ([Math]::Max(0, $beforeBytes - $afterBytes)))"
}
Write-Host ''
Write-Host 'Worktrees under .worktrees/ are not removed automatically.'
Write-Host 'Example: git worktree remove .worktrees/feature/powershell-to-csharp'
