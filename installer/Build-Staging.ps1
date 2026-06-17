#Requires -Version 5.1
<#
.SYNOPSIS
  Build installer staging folder (Phase 5 P5A).

.DESCRIPTION
  1. Publish Release binaries to repo bin\
  2. Generate lib assets (xaml, ico)
  3. Copy install payload to installer\staging\
  4. Download .NET Desktop Runtime redist if missing
  5. Validate layout via Test-InstallerStagingLayout
#>
param(
    [string]$Configuration = 'Release',
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [switch]$SkipPublish,
    [switch]$SkipRedistDownload,
    [string]$StagingDir = (Join-Path $PSScriptRoot 'staging')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'InstallStaging.ps1')

function Get-InstallerAppVersion {
    param([string]$InstallerDir)
    $versionFile = Join-Path $InstallerDir 'version.txt'
    if (-not (Test-Path -LiteralPath $versionFile)) {
        throw "Missing installer version file: $versionFile"
    }
    return (Get-Content -LiteralPath $versionFile -Raw).Trim()
}

function Get-InstallerRuntimeVersion {
    param([string]$InstallerDir)
    $runtimeFile = Join-Path $InstallerDir 'runtime-version.txt'
    if (-not (Test-Path -LiteralPath $runtimeFile)) {
        throw "Missing installer runtime version file: $runtimeFile"
    }
    return (Get-Content -LiteralPath $runtimeFile -Raw).Trim()
}

function Test-ValidRuntimeRedist {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    return (Get-Item -LiteralPath $Path).Length -gt 50MB
}

function Reset-InstallerStaging {
    param([string]$Path)
    $redistBackup = $null
    $redistDir = Join-Path $Path 'redist'
    if (Test-Path -LiteralPath $redistDir) {
        $redistBackup = Join-Path $env:TEMP ('sg-redist-backup-' + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $redistDir -Destination $redistBackup -Force
    }
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    if ($redistBackup -and (Test-Path -LiteralPath $redistBackup)) {
        Move-Item -LiteralPath $redistBackup -Destination $redistDir -Force
    }
}

function Copy-InstallerPayload {
    param(
        [string]$Root,
        [string]$StagingDir
    )

    $binSource = Join-Path $Root 'bin'
    if (-not (Test-Path -LiteralPath $binSource)) {
        throw "Publish output not found: $binSource (run Publish-All first)"
    }

    Copy-Item -LiteralPath $binSource -Destination (Join-Path $StagingDir 'bin') -Recurse -Force

    $libDest = Join-Path $StagingDir 'lib'
    New-Item -ItemType Directory -Path $libDest -Force | Out-Null
    foreach ($asset in @('SmartGuard.ico', 'SmartGuard.Settings.xaml')) {
        $src = Join-Path $Root "lib\$asset"
        if (-not (Test-Path -LiteralPath $src)) {
            throw "Missing required asset: $src"
        }
        Copy-Item -LiteralPath $src -Destination (Join-Path $libDest $asset) -Force
    }

    $license = Join-Path $PSScriptRoot 'license_zh-CN.txt'
    Copy-Item -LiteralPath $license -Destination (Join-Path $StagingDir 'license_zh-CN.txt') -Force
}

function Ensure-DotNetDesktopRuntimeRedist {
    param(
        [string]$StagingDir,
        [string]$RuntimeVersion,
        [switch]$SkipDownload
    )

    $redistDir = Join-Path $StagingDir 'redist'
    New-Item -ItemType Directory -Path $redistDir -Force | Out-Null

    $fileName = "windowsdesktop-runtime-$RuntimeVersion-win-x64.exe"
    $dest = Join-Path $redistDir $fileName
    $marker = Join-Path $redistDir 'runtime-installer.txt'

    if (Test-ValidRuntimeRedist -Path $dest) {
        Set-Content -LiteralPath $marker -Value $fileName -Encoding ASCII -NoNewline
        return $fileName
    }
    if (Test-Path -LiteralPath $dest) {
        Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue
    }

    if ($SkipDownload) {
        throw "Desktop runtime redist not found: $dest (omit -SkipRedistDownload to fetch)"
    }

    $url = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$RuntimeVersion/$fileName"
    Write-Host "Downloading $url ..."
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & curl.exe -L --retry 3 --connect-timeout 30 --max-time 900 -o $dest $url
        if ($LASTEXITCODE -ne 0) {
            throw "curl download failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing -TimeoutSec 900
    }
    if (-not (Test-ValidRuntimeRedist -Path $dest)) {
        throw "Download incomplete or corrupt: $dest"
    }

    Set-Content -LiteralPath $marker -Value $fileName -Encoding ASCII -NoNewline
    return $fileName
}

$appVersion = Get-InstallerAppVersion -InstallerDir $PSScriptRoot
$runtimeVersion = Get-InstallerRuntimeVersion -InstallerDir $PSScriptRoot

Write-Host "SmartGuard installer staging (app $appVersion, runtime $runtimeVersion, P5A)"

if (-not $SkipPublish) {
    $publish = Join-Path $Root 'scripts\Publish-All.ps1'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $publish -Configuration $Configuration -Root $Root
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$xamlScript = Join-Path $Root 'lib\Write-SmartGuardSettingsXaml.ps1'
& powershell -NoProfile -ExecutionPolicy Bypass -File $xamlScript
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iconScript = Join-Path $Root 'lib\Create-TrayIcon.ps1'
$iconPath = Join-Path $Root 'lib\SmartGuard.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $iconScript
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Reset-InstallerStaging -Path $StagingDir
Copy-InstallerPayload -Root $Root -StagingDir $StagingDir
$runtimeFile = Ensure-DotNetDesktopRuntimeRedist -StagingDir $StagingDir -RuntimeVersion $runtimeVersion -SkipDownload:$SkipRedistDownload
Set-Content -LiteralPath (Join-Path $StagingDir 'VERSION.txt') -Value $appVersion -Encoding ASCII -NoNewline

Test-InstallerStagingLayout -StagingDir $StagingDir -RequireRedist
Write-Host "Staging ready: $StagingDir (runtime: $runtimeFile)"
