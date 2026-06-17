param(
    [string]$Configuration = 'Release',
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$engine = Join-Path $PSScriptRoot 'Publish-Engine.ps1'
$tray = Join-Path $PSScriptRoot 'Publish-Tray.ps1'
$logViewer = Join-Path $PSScriptRoot 'Publish-LogViewer.ps1'
$settings = Join-Path $PSScriptRoot 'Publish-Settings.ps1'

& $engine -Configuration $Configuration -Root $Root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $tray -Configuration $Configuration -Root $Root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $logViewer -Configuration $Configuration -Root $Root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $settings -Configuration $Configuration -Root $Root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Published Engine + Tray + LogViewer + Settings to: $(Join-Path $Root 'bin')"
