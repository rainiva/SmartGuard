#Requires -Version 5.1
param([string]$ScriptRoot = 'C:\Tools')

$lib = Join-Path $ScriptRoot 'lib'

function Fix-EncodingUtf8Bom {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) { return }
    $nullCount = 0
    foreach ($b in $bytes) {
        if ($b -eq 0) { $nullCount++ }
    }
    if ($nullCount -gt ($bytes.Length / 4)) {
        $text = [System.Text.Encoding]::Unicode.GetString($bytes)
    }
    else {
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    }
    $enc = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($Path, $text, $enc)
    Write-Host ('Fixed: ' + $Path)
}

$items = Get-ChildItem -Path $ScriptRoot -Recurse -Filter '*.ps1' -File |
    Where-Object { $_.Name -notlike '_*' }
foreach ($item in $items) {
    Fix-EncodingUtf8Bom -Path $item.FullName
}

$writer = Join-Path $lib 'Write-SmartPowerPlanSettingsXaml.ps1'
Fix-EncodingUtf8Bom -Path $writer
. $writer -ScriptRoot $ScriptRoot
Write-Host 'Repair complete.'
