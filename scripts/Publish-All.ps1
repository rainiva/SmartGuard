param(
    [string]$Configuration = 'Release',
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$build = Join-Path $Root 'build.cmd'
& cmd /c "`"$build`" $Configuration"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Published Engine + Tray + LogViewer + Settings to: $(Join-Path $Root 'bin')"
