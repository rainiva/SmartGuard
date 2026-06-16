param(
    [string]$Configuration = 'Release',
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\SmartGuard.Engine\SmartGuard.Engine.csproj'
$outDir = Join-Path $Root 'bin'

Write-Host "Publishing SmartGuard.Engine ($Configuration, framework-dependent)..."
dotnet publish $project -c $Configuration -r win-x64 --self-contained false -o $outDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to: $outDir\SmartGuard.Engine.exe"
