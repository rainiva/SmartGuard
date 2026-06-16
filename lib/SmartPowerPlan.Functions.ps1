# SmartPowerPlan.Functions.ps1 — 分层模块加载入口

$_layersRoot = Join-Path $PSScriptRoot 'layers'
if (Test-Path $_layersRoot) {
    @(
        'Infrastructure.TextIo.ps1',
        'Infrastructure.Paths.ps1',
        'Domain.PowerPlan.ps1',
        'Domain.Idempotency.ps1',
        'Domain.StatusEvents.ps1',
        'Infrastructure.PowerCfg.ps1',
        'Infrastructure.Logging.ps1',
        'Infrastructure.Config.ps1',
        'Infrastructure.Process.ps1',
        'Infrastructure.StatusStore.ps1',
        'Infrastructure.AutoStart.ps1',
        'Infrastructure.Toast.ps1',
        'Presentation.LogViewer.ps1'
    ) | ForEach-Object {
        $path = Join-Path $_layersRoot $_
        if (Test-Path -LiteralPath $path) { . $path }
    }
}
