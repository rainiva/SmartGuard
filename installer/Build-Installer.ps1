#Requires -Version 5.1
<#
.SYNOPSIS
  Build staging and compile SmartGuard Inno Setup installer.
#>
param(
    [string]$Configuration = 'Release',
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [switch]$SkipPublish,
    [switch]$SkipRedistDownload,
    [switch]$SkipStaging,
    [switch]$SkipVersionBump,
    [string]$StagingDir = (Join-Path $PSScriptRoot 'staging'),
    [string]$IsccPath = $env:SMARTGUARD_ISCC
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'InstallVersion.ps1')

function Resolve-InnoSetupCompiler {
    param([string]$Preferred)

    if ($Preferred -and (Test-Path -LiteralPath $Preferred)) {
        return $Preferred
    }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        "D:\Apps\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) { return $path }
    }

    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    throw 'ISCC.exe not found. Install Inno Setup 6 or set SMARTGUARD_ISCC to ISCC.exe path.'
}

if (-not $SkipStaging) {
    $stagingScript = Join-Path $PSScriptRoot 'Build-Staging.ps1'
    $args = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $stagingScript,
        '-Configuration', $Configuration,
        '-Root', $Root,
        '-StagingDir', $StagingDir
    )
    if ($SkipPublish) { $args += '-SkipPublish' }
    if ($SkipRedistDownload) { $args += '-SkipRedistDownload' }
    & powershell @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$versionFile = Join-Path $PSScriptRoot 'version.txt'
$version = Update-InstallerVersionFile -VersionFile $versionFile -SkipBump:$SkipVersionBump
if (-not $SkipVersionBump) {
    Write-Host "Installer version bumped to $version"
}
$runtimeMarker = Join-Path $StagingDir 'redist\runtime-installer.txt'
if (-not (Test-Path -LiteralPath $runtimeMarker)) {
    throw "Missing runtime marker: $runtimeMarker (run Build-Staging first)"
}
$runtimeFile = (Get-Content -LiteralPath $runtimeMarker -Raw).Trim()

$iscc = Resolve-InnoSetupCompiler -Preferred $IsccPath
$iss = Join-Path $PSScriptRoot 'SmartGuard.iss'
$stagingAbs = (Resolve-Path -LiteralPath $StagingDir).Path

Write-Host "Compiling installer with: $iscc"
& $iscc `
    "/DStagingDir=$stagingAbs" `
    "/DMyAppVersion=$version" `
    "/DRuntimeInstallerFile=$runtimeFile" `
    $iss

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dist = Join-Path $Root 'dist'
Write-Host "Installer output: $dist\SmartGuard-Setup-$version-x64.exe"
