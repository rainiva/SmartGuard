param(
    [string]$Configuration = 'Release',
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\SmartGuard.Settings\SmartGuard.Settings.csproj'
$outDir = Join-Path $Root 'bin'

Write-Host "Publishing SmartGuard.Settings ($Configuration, framework-dependent)..."
dotnet publish $project -c $Configuration -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $outDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to: $outDir\SmartGuard.Settings.exe"
