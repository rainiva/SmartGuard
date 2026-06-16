#Requires -RunAsAdministrator
#Requires -Version 5.1
$toolsRoot = 'D:\Project\SmartGuard'
$lib = Join-Path $toolsRoot 'lib'

function Repair-ScriptEncodingUtf8Bom {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return }
    $nullCount = ($bytes | Where-Object { $_ -eq 0 }).Count
    $text = if ($nullCount -gt $bytes.Length / 4) {
        [System.Text.Encoding]::Unicode.GetString($bytes)
    } else {
        [System.Text.Encoding]::UTF8.GetString($bytes)
    }
    [System.IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding $true))
    Write-Host "UTF-8 BOM: $Path"
}

$paths = @(
    (Join-Path $toolsRoot 'Register-SmartGuardTask.ps1'),
    (Join-Path $toolsRoot 'Run-Tests.ps1')
)
Get-ChildItem -Path $lib -Filter '*.ps1' -File | ForEach-Object { $paths += $_.FullName }
$testsDir = Join-Path $toolsRoot 'Tests'
if (Test-Path $testsDir) {
    Get-ChildItem -Path $testsDir -Filter '*.ps1' -File | ForEach-Object { $paths += $_.FullName }
}
$paths | Select-Object -Unique | ForEach-Object { Repair-ScriptEncodingUtf8Bom -Path $_ }

& (Join-Path $lib 'Create-TrayIcon.ps1')
& (Join-Path $lib 'Write-SmartGuardSettingsXaml.ps1') -ScriptRoot $toolsRoot
& (Join-Path $toolsRoot 'Register-SmartGuardTask.ps1')
& (Join-Path $lib 'Register-TrayTask.ps1')

Write-Host ''
Write-Host '智能电源守护安装完成。'
Write-Host '  核心任务：SmartGuard Guardian'
Write-Host '  托盘任务：SmartGuard Tray'
Write-Host '  测试：powershell -ExecutionPolicy Bypass -File D:\Project\SmartGuard\Run-Tests.ps1'