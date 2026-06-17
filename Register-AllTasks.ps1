# Register Core + Tray scheduled tasks via C# engine install
#Requires -Version 5.1
$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$engineExe = Join-Path $root 'bin\SmartGuard.Engine.exe'

if (-not (Test-Path -LiteralPath $engineExe)) {
    Write-Error "SmartGuard.Engine.exe not found at $engineExe"
    exit 1
}

Write-Host 'Registering scheduled tasks (admin required)...'
& $engineExe --root $root --install --skip-publish
if ($LASTEXITCODE -ne 0) {
    Write-Error "Task registration failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host 'Done. Log off and log on, or run Start-Tray.cmd manually.'
