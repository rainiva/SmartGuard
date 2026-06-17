# One-time migration: SmartPowerPlan -> SmartGuard at D:\Project\SmartGuard
$ErrorActionPreference = 'Stop'
$root = 'D:\Project\SmartGuard'
Set-Location $root

# --- rename files (deepest first) ---
$renames = @(
    @{ Old = 'lib\SmartPowerPlan.Settings.ps1.source'; New = 'lib\SmartGuard.Settings.ps1.source' }
    @{ Old = 'lib\SmartPowerPlan.Settings.xaml'; New = 'lib\SmartGuard.Settings.xaml' }
    @{ Old = 'lib\SmartPowerPlan.Settings.ps1'; New = 'lib\SmartGuard.Settings.ps1' }
    @{ Old = 'lib\SmartPowerPlan.Tray.ps1'; New = 'lib\SmartGuard.Tray.ps1' }
    @{ Old = 'lib\SmartPowerPlan.Core.ps1'; New = 'lib\SmartGuard.Core.ps1' }
    @{ Old = 'lib\SmartPowerPlan.Functions.ps1'; New = 'lib\SmartGuard.Functions.ps1' }
    @{ Old = 'lib\Write-SmartPowerPlanSettingsXaml.ps1'; New = 'lib\Write-SmartGuardSettingsXaml.ps1' }
    @{ Old = 'lib\Repair-SmartPowerPlanEncoding.ps1'; New = 'lib\Repair-SmartGuardEncoding.ps1' }
    @{ Old = 'lib\Install-SmartPowerPlan.ps1'; New = 'lib\Install-SmartGuard.ps1' }
    @{ Old = 'lib\Start-SmartPowerPlan.ps1'; New = 'lib\Start-SmartGuard.ps1' }
    @{ Old = 'Tests\SmartPowerPlan.Tests.ps1'; New = 'Tests\SmartGuard.Tests.ps1' }
    @{ Old = 'SmartPowerPlan.Functions.ps1'; New = 'SmartGuard.Functions.ps1' }
    @{ Old = 'SmartPowerPlan.Core.ps1'; New = 'SmartGuard.Core.ps1' }
    @{ Old = 'Start-SmartPowerPlan.ps1'; New = 'Start-SmartGuard.ps1' }
    @{ Old = 'Register-SmartPowerPlanTask.ps1'; New = 'Register-SmartGuardTask.ps1' }
    @{ Old = 'SmartPowerPlan.config.json'; New = 'SmartGuard.config.json' }
    @{ Old = 'SmartPowerPlan.status.json'; New = 'SmartGuard.status.json' }
    @{ Old = '.SmartPowerPlan.initialized'; New = '.SmartGuard.initialized' }
)
foreach ($r in $renames) {
    $oldPath = Join-Path $root $r.Old
    $newPath = Join-Path $root $r.New
    if (Test-Path -LiteralPath $oldPath) {
        if (Test-Path -LiteralPath $newPath) { Remove-Item -LiteralPath $newPath -Force }
        Rename-Item -LiteralPath $oldPath -NewName (Split-Path -Leaf $r.New)
        Write-Host "Renamed: $($r.Old) -> $($r.New)"
    }
}

# icon if exists
$iconOld = Join-Path $root 'lib\SmartPowerPlan.ico'
$iconNew = Join-Path $root 'lib\SmartGuard.ico'
if (Test-Path -LiteralPath $iconOld) {
    if (Test-Path -LiteralPath $iconNew) { Remove-Item -LiteralPath $iconNew -Force }
    Rename-Item -LiteralPath $iconOld -NewName 'SmartGuard.ico'
}

# --- content replace in text files ---
$extensions = @('*.ps1', '*.ps1.source', '*.cmd', '*.json', '*.xaml', '*.txt', '*.md', '*.py', '*.gitignore')
$files = Get-ChildItem -Path $root -Recurse -File -Include $extensions |
    Where-Object { $_.FullName -notmatch '\\scripts\\Migrate-Rename' -and $_.FullName -notmatch '\\src\\' -and $_.FullName -notmatch '\\tests\\SmartGuard\.Engine' }

$replacements = @(
    @{ Pattern = 'C:\\Tools'; Replacement = 'D:\Project\SmartGuard' }
    @{ Pattern = 'c:\\Tools'; Replacement = 'D:\Project\SmartGuard' }
    @{ Pattern = 'C:/Tools'; Replacement = 'D:/Project/SmartGuard' }
    @{ Pattern = 'Get-SmartPowerPlan'; Replacement = 'Get-SmartGuard' }
    @{ Pattern = 'Read-SmartPowerPlan'; Replacement = 'Read-SmartGuard' }
    @{ Pattern = 'Save-SmartPowerPlan'; Replacement = 'Save-SmartGuard' }
    @{ Pattern = 'Write-SmartPowerPlan'; Replacement = 'Write-SmartGuard' }
    @{ Pattern = 'Test-SmartPowerPlan'; Replacement = 'Test-SmartGuard' }
    @{ Pattern = 'New-SmartPowerPlan'; Replacement = 'New-SmartGuard' }
    @{ Pattern = 'Initialize-SmartPowerPlan'; Replacement = 'Initialize-SmartGuard' }
    @{ Pattern = 'Start-SmartPowerPlan'; Replacement = 'Start-SmartGuard' }
    @{ Pattern = 'Show-SmartPowerPlan'; Replacement = 'Show-SmartGuard' }
    @{ Pattern = 'Install-SmartPowerPlan'; Replacement = 'Install-SmartGuard' }
    @{ Pattern = 'Repair-SmartPowerPlan'; Replacement = 'Repair-SmartGuard' }
    @{ Pattern = 'Register-SmartPowerPlan'; Replacement = 'Register-SmartGuard' }
    @{ Pattern = 'Invoke-SmartPowerPlan'; Replacement = 'Invoke-SmartGuard' }
    @{ Pattern = 'Get-DefaultSmartPowerPlan'; Replacement = 'Get-DefaultSmartGuard' }
    @{ Pattern = 'Open-TrayLogViewer'; Replacement = 'Open-TrayLogViewer' }
    @{ Pattern = 'SmartPowerPlan\.config\.json'; Replacement = 'SmartGuard.config.json' }
    @{ Pattern = 'SmartPowerPlan\.status\.json'; Replacement = 'SmartGuard.status.json' }
    @{ Pattern = 'SmartPowerPlan\.startup\.log'; Replacement = 'SmartGuard.startup.log' }
    @{ Pattern = 'SmartPowerPlan\.ico'; Replacement = 'SmartGuard.ico' }
    @{ Pattern = '\.SmartPowerPlan\.initialized'; Replacement = '.SmartGuard.initialized' }
    @{ Pattern = 'SmartPowerPlan Guardian'; Replacement = 'SmartGuard Guardian' }
    @{ Pattern = 'SmartPowerPlan Tray'; Replacement = 'SmartGuard Tray' }
    @{ Pattern = 'SmartPowerPlan\.Functions'; Replacement = 'SmartGuard.Functions' }
    @{ Pattern = 'SmartPowerPlan\.Core'; Replacement = 'SmartGuard.Core' }
    @{ Pattern = 'SmartPowerPlan\.Settings'; Replacement = 'SmartGuard.Settings' }
    @{ Pattern = 'SmartPowerPlan\.Tray'; Replacement = 'SmartGuard.Tray' }
    @{ Pattern = 'SmartPowerPlanWpfApplication'; Replacement = 'SmartGuardWpfApplication' }
    @{ Pattern = 'Describe ''SmartPowerPlan'''; Replacement = "Describe 'SmartGuard'" }
    @{ Pattern = '智能电源计划'; Replacement = '智能电源守护' }
    @{ Pattern = 'SmartPowerPlan'; Replacement = 'SmartGuard' }
)

foreach ($file in $files) {
    $content = [IO.File]::ReadAllText($file.FullName)
    $original = $content
    foreach ($r in $replacements) {
        $content = [regex]::Replace($content, $r.Pattern, $r.Replacement)
    }
    # restore log filename (user requested keep SmartPowerPlan.log)
    $content = $content -replace 'SmartGuard\.log', 'SmartPowerPlan.log'
    if ($content -ne $original) {
        [IO.File]::WriteAllText($file.FullName, $content, [Text.UTF8Encoding]::new($true))
        Write-Host "Updated: $($file.FullName)"
    }
}

Write-Host 'Migration rename complete.'
