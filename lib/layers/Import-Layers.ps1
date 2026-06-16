# SmartPowerPlan 分层模块加载器（Domain -> Infrastructure -> Presentation）
$layersRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Get-ChildItem -LiteralPath $layersRoot -Filter '*.ps1' -File |
    Where-Object { $_.Name -ne 'Import-Layers.ps1' } |
    Sort-Object Name |
    ForEach-Object { . $_.FullName }
