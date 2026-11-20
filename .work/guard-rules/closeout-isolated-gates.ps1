#Requires -RunAsAdministrator
param([ValidateSet('Performance','Desktop')][string]$Mode)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$root = Join-Path $PSScriptRoot 'closeout-desktop-root'
. (Join-Path $repo 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
$snapshot = Capture-SmartGuardScheduledTasks
$tasks = @('SmartGuard Guardian','SmartGuard Tray') | ForEach-Object {
  $t = Get-ScheduledTask -TaskName $_ -ErrorAction SilentlyContinue
  if ($t) { [pscustomobject]@{ Name=$_; Enabled=[bool]$t.Settings.Enabled; Actions=@($t.Actions | Select-Object Execute,Arguments,WorkingDirectory) } }
}
$originalPower = (& powercfg.exe /getactivescheme) -join ' '
if ($originalPower -notmatch '[a-fA-F0-9]{8}(-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}') { throw 'Power baseline missing' }
$originalPlan = $Matches[0]
$baselineRoot = Resolve-SmartGuardRestoreRootFromSnapshot -Snapshot $snapshot
$baselineConfig = Join-Path $baselineRoot 'SmartGuard.config.json'
$baselineHash = (Get-FileHash -LiteralPath $baselineConfig).Hash
$resultPath = Join-Path $PSScriptRoot ('closeout-'+$Mode.ToLowerInvariant()+'-result.json')
$owned = @()
$result = [ordered]@{ mode=$Mode; startedUtc=[DateTime]::UtcNow.ToString('O'); stage='preparing'; baselineRoot=$baselineRoot; baselineConfigHash=$baselineHash; originalPlan=$originalPlan; tasks=$tasks; exitCode=$null; error=''; restored=$false }
function Save-Receipt { [IO.File]::WriteAllText($resultPath,($result|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false)) }
try {
  # Definitions remain intact. Suspend the captured production instance only for
  # this bounded acceptance session; restore its actual prior process state.
  foreach($t in $tasks) { Disable-ScheduledTask -TaskName $t.Name | Out-Null; Stop-ScheduledTask -TaskName $t.Name }
  Stop-SmartGuardProcessesWithoutElevation
  if($Mode -eq 'Performance') {
    $result.stage='testing'; Save-Receipt
    & dotnet test (Join-Path $repo 'Tests\SmartGuard.Engine.PerformanceTests\SmartGuard.Engine.PerformanceTests.csproj') -c Release --logger 'trx;LogFileName=closeout-performance.trx' --results-directory (Join-Path $repo '.work\guard-rules') --nologo
    $result.exitCode=$LASTEXITCODE
  } else {
    $engine=Start-Process -FilePath (Join-Path $root 'bin\SmartGuard.Engine.exe') -ArgumentList @('--root',$root) -WorkingDirectory $root -WindowStyle Hidden -PassThru
    $owned += $engine
    $tray=Start-Process -FilePath (Join-Path $root 'bin\SmartGuard.Tray.exe') -ArgumentList @('--root',$root) -WorkingDirectory $root -WindowStyle Hidden -PassThru
    $owned += $tray
    $result.enginePid=$engine.Id; $result.trayPid=$tray.Id; $result.stage='active'; Save-Receipt
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while($watch.Elapsed.TotalSeconds -lt 900 -and -not(Test-Path -LiteralPath (Join-Path $root 'stop.request'))) {
      if($engine.HasExited) { throw ('Owned Engine exited '+$engine.ExitCode) }
      Start-Sleep -Seconds 1
    }
    $result.exitCode=0
  }
} catch { $result.error=$_.Exception.Message; $result.exitCode=1 }
finally {
  foreach($p in $owned) { if(-not $p.HasExited) { Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(5000) } }
  & powercfg.exe /setactive $originalPlan
  if($LASTEXITCODE -ne 0) { $result.error += '; power restore failed'; $result.exitCode=1 }
  foreach($t in $tasks) { if($t.Enabled) { Enable-ScheduledTask -TaskName $t.Name | Out-Null } }
  Start-SmartGuardRestoredTasks -Root $baselineRoot -Snapshot $snapshot
  if((Get-FileHash -LiteralPath $baselineConfig).Hash -ne $baselineHash) { $result.error += '; baseline config changed'; $result.exitCode=1 }
  $result.restored=$true; $result.stage='restored'; $result.completedUtc=[DateTime]::UtcNow.ToString('O'); Save-Receipt
}
exit $result.exitCode
