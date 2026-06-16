# Infrastructure: status.json 原子写入

function Write-SmartPowerPlanStatusAtomic {
    param(
        [hashtable]$Status,
        [string]$StatusPath
    )
    if ([string]::IsNullOrWhiteSpace($StatusPath)) { return }
    $dir = Split-Path -Parent $StatusPath
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $json = $Status | ConvertTo-Json -Depth 6
    $tmp = "$StatusPath.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [IO.File]::WriteAllText($tmp, $json, $utf8)
        if (Test-Path $StatusPath) {
            Move-Item -LiteralPath $tmp -Destination $StatusPath -Force
        }
        else {
            Move-Item -LiteralPath $tmp -Destination $StatusPath
        }
    }
    catch {
        if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
        throw
    }
}
