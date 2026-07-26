param(
    [Parameter(Mandatory)]
    [string]$RepoRoot,
    [Parameter(Mandatory)]
    [string]$TestScriptPath,
    [Parameter(Mandatory)]
    [string]$DeclinedMarkerPath,
    [Parameter(Mandatory)]
    [string]$TranscriptPath,
    [Parameter(Mandatory)]
    [string]$ResultPath,
    [string]$Tier = 'full'
)

$ErrorActionPreference = 'Stop'
if (-not [string]::IsNullOrWhiteSpace($DeclinedMarkerPath)) {
    Remove-Item -LiteralPath $DeclinedMarkerPath -Force -ErrorAction SilentlyContinue
}

$runner = Join-Path $RepoRoot 'scripts\Invoke-ElevatedPesterFile.ps1'
& $runner `
    -TestPath $TestScriptPath `
    -ResultPath $ResultPath `
    -TranscriptPath $TranscriptPath `
    -Tier $Tier
exit $LASTEXITCODE
