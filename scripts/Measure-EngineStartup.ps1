#Requires -Version 5.1
$root = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { 'D:\Project\SmartGuard' }
. (Join-Path $PSScriptRoot '..\Tests\Integration\SmartGuardStop.ps1')

$exe = Join-Path $root 'bin\SmartGuard.Engine.exe'
$log = Join-Path $root 'SmartGuard.log'

Stop-SmartGuardProcesses -EngineExe $exe -Root $root
Start-Sleep -Seconds 1

$logBytesBefore = if (Test-Path -LiteralPath $log) { (Get-Item -LiteralPath $log).Length } else { 0 }
$marker = "benchmark-{0}" -f [Guid]::NewGuid().ToString('N')

$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $exe -ArgumentList '--root', $root -WindowStyle Hidden -PassThru
$found = $false
while ($sw.ElapsedMilliseconds -lt 10000) {
    $proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
    if (-not $proc) { break }
    if (Test-Path -LiteralPath $log) {
        $len = (Get-Item -LiteralPath $log).Length
        if ($len -gt $logBytesBefore) {
            $tail = Get-Content -LiteralPath $log -Tail 3 -ErrorAction SilentlyContinue | Out-String
            if ($tail -match 'SmartGuard Engine 启动') {
                $found = $true
                break
            }
        }
    }
    Start-Sleep -Milliseconds 50
}
$sw.Stop()

Start-Sleep -Seconds 1
$proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
$memMb = if ($proc) { [math]::Round($proc.WorkingSet64 / 1MB, 1) } else { 0 }
Write-Output "StartupMs=$($sw.ElapsedMilliseconds)"
Write-Output "FirstLogFound=$found"
Write-Output "MemoryMB=$memMb"
Write-Output "StillRunning=$([bool]$proc)"
Stop-SmartGuardProcesses -EngineExe $exe -Root $root
