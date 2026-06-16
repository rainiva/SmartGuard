$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { 'D:\Project\SmartGuard' }
$lib = Join-Path $root 'lib'

function Write-BomFile {
    param([string]$Path, [string]$Content)
    $parent = Split-Path $Path -Parent
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $enc = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($Path, $Content, $enc)
}

function Repair-FileEncoding {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254) {
        $text = [System.Text.Encoding]::Unicode.GetString($bytes)
        Write-BomFile -Path $Path -Content $text
        Write-Host ('Repaired UTF-16 BOM: ' + $Path)
        return $true
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
        return $false
    }
    $nulls = 0
    foreach ($b in $bytes) {
        if ($b -eq 0) { $nulls++ }
    }
    if ($nulls -gt ($bytes.Length / 4)) {
        $text = [System.Text.Encoding]::Unicode.GetString($bytes)
        Write-BomFile -Path $Path -Content $text
        Write-Host ('Repaired UTF-16: ' + $Path)
        return $true
    }
    return $false
}

$settingsPath = Join-Path $lib 'SmartGuard.Settings.ps1'
$settingsSource = Join-Path $lib 'SmartGuard.Settings.ps1.source'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    if (Test-Path -LiteralPath $settingsSource) {
        Copy-Item -LiteralPath $settingsSource -Destination $settingsPath -Force
        Write-Host ('Restored settings from source: ' + $settingsPath)
    }
    else {
        throw ('Missing settings module: ' + $settingsPath)
    }
}

$items = Get-ChildItem -Path $lib -Filter '*.ps1' -File
foreach ($item in $items) {
    Repair-FileEncoding -Path $item.FullName | Out-Null
}

$layerItems = Get-ChildItem -Path (Join-Path $lib 'layers') -Filter '*.ps1' -File -ErrorAction SilentlyContinue
foreach ($item in $layerItems) {
    Repair-FileEncoding -Path $item.FullName | Out-Null
}

$writer = Join-Path $lib 'Write-SmartGuardSettingsXaml.ps1'
Repair-FileEncoding -Path $writer | Out-Null
. $writer -ScriptRoot $root
Write-Host 'Bootstrap complete.'
