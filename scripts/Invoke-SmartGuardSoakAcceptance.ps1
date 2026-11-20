#Requires -RunAsAdministrator
#Requires -Version 7.2
param(
 [Parameter(Mandatory)][string]$Root,
 [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$EvidenceName,
 [ValidateRange(1,480)][int]$SoakMinutes=480
)
# Raw evidence only: a completed collection still requires independent review.
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root=[IO.Path]::GetFullPath($Root)
$evidenceDirectory=Join-Path $repo '.work/guard-rules'
$receipt=Join-Path $evidenceDirectory ($EvidenceName+'-session.result.json')
if(Test-Path $receipt){throw 'Evidence already exists'}
if(Test-Path (Join-Path $root 'SmartGuard.status.json')){throw 'Candidate root already has state; use fresh evidence'}
Set-Location $repo
. ./Tests/Integration/Restore-SmartGuardScheduledTasks.ps1
. (Join-Path $PSScriptRoot 'Read-SmartGuardAcceptanceStatus.ps1')
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class SoakPowerRead {
 [DllImport("Powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr key,ref Guid scheme,ref Guid subgroup,ref Guid setting,out uint value);
 public static uint Read(Guid scheme,Guid subgroup,Guid setting) { uint value; var error=PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,out value); if(error!=0) throw new Win32Exception((int)error); return value; }
}
'@
$snapshot=Capture-SmartGuardScheduledTasks
$priorRoot=Resolve-SmartGuardRestoreRootFromSnapshot $snapshot
$configPath=Join-Path $priorRoot 'SmartGuard.config.json'
$configHash=(Get-FileHash $configPath).Hash
$power=(& powercfg.exe /getactivescheme)-join ' '
if($power -notmatch '[a-fA-F0-9]{8}(-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}'){throw 'Missing plan'}
$plan=$Matches[0]
$brightness=@(Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightness|Select-Object InstanceName,CurrentBrightness)
$tasks=@('SmartGuard Guardian','SmartGuard Tray')|ForEach-Object{$t=Get-ScheduledTask -TaskName $_;[pscustomobject]@{Name=$_;Enabled=[bool]$t.Settings.Enabled}}
$schemes=@('8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c','381b4222-f694-41f0-9685-ff5bb260df2e','a1841308-3541-4fab-bc81-f71556f20b4a')
$settings=@(@{Sub='238c9fa8-0aad-41ed-83f4-97be242c8f20';Setting='29f6c1db-86da-48c5-9fdb-f2b67b1f44da'},@{Sub='7516b95f-f776-4464-8c53-06167f40cc99';Setting='3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e'})
$indices=@(foreach($scheme in $schemes){foreach($setting in $settings){[pscustomobject]@{Scheme=$scheme;Sub=$setting.Sub;Setting=$setting.Setting;Value=[SoakPowerRead]::Read([guid]$scheme,[guid]$setting.Sub,[guid]$setting.Setting)}}})
$sampleCount=0
$samplesFile=Join-Path $evidenceDirectory ($EvidenceName+'-samples.jsonl')
if(Test-Path $samplesFile){throw 'Samples already exist'}
$result=[ordered]@{Stage='preparing';Result='Running';StartedUtc=[DateTime]::UtcNow.ToString('O');SoakMinutes=$SoakMinutes;StatusReadRetryTotal=0;Before=$snapshot;After=$null;PowerPlan=$plan;Brightness=$brightness;AcIndices=$indices;ConfigHash=$configHash;EnginePid=$null;TrayPid=$null;EpochUtc=$null;SoakStartedUtc=$null;CompletedUtc=$null;Restored=$false;Error='';SamplesFile=$samplesFile;SampleCount=0;LatestSample=$null;Health=@()}
function Save-Receipt{[IO.File]::WriteAllText($receipt,($result|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))}
function Write-AcIndex($index,$value){& powercfg.exe /setacvalueindex $index.Scheme $index.Sub $index.Setting $value;if($LASTEXITCODE -ne 0){throw 'AC setting write failed'}}
Save-Receipt
$owned=@();$watch=[Diagnostics.Stopwatch]::StartNew();$nextSample=0.0;$soakAt=$null
try{
 foreach($task in $tasks){Disable-ScheduledTask -TaskName $task.Name|Out-Null;Stop-ScheduledTask -TaskName $task.Name}
 Stop-SmartGuardProcessesWithoutElevation
 foreach($index in $indices){Write-AcIndex $index 0}
 & powercfg.exe /setactive $plan;if($LASTEXITCODE -ne 0){throw 'Apply observation power settings failed'}
 foreach($name in @('SmartGuard.Engine.exe','SmartGuard.Tray.exe')){$p=Start-Process -FilePath (Join-Path $root ('bin/'+$name)) -ArgumentList @('--root',(ConvertTo-SmartGuardQuotedProcessArgument $root)) -WorkingDirectory $root -WindowStyle Hidden -PassThru;$owned+=$p}
 $result.EnginePid=$owned[0].Id;$result.TrayPid=$owned[1].Id;$result.Stage='preparing-first-sample';Save-Receipt
 while(-not(Test-Path (Join-Path $root 'SmartGuard.status.json')) -or -not(Test-Path (Join-Path $root 'SmartGuard.config.json'))){if($watch.Elapsed.TotalSeconds -gt 30){throw 'Initial status/config timed out'};if($owned[0].HasExited -or $owned[1].HasExited){throw 'Candidate exited during startup'};Start-Sleep -Milliseconds 200}
 $result.EpochUtc=[DateTime]::UtcNow.ToString('O');$result.SoakStartedUtc=$result.EpochUtc;$result.Stage='observing-continuous-run';$watch.Restart();$soakAt=0.0;Save-Receipt
 while($watch.Elapsed.TotalMinutes -lt ($SoakMinutes+30) -and $sampleCount -lt 2040){
  if(Test-Path (Join-Path $root 'stop.request')){throw 'Observation cancelled'}
  if($owned[0].HasExited -or $owned[1].HasExited){throw 'Candidate exited'}
  if($watch.Elapsed.TotalSeconds -ge $nextSample){
   $nextSample=$watch.Elapsed.TotalSeconds+15
   $read=Read-SmartGuardAcceptanceStatus -StatusPath (Join-Path $root 'SmartGuard.status.json') -BinDirectory (Join-Path $root 'bin') -ExpectedEnginePid $owned[0].Id
   $status=$read.Status
   if(-not $configReader){$configReader=[SmartGuard.Configuration.GuardConfigRepository]::new((Join-Path $root 'SmartGuard.config.json'))}
   $cfg=$null
   for($configAttempt=1;$configAttempt -le 6;$configAttempt++){$cfg=$configReader.TryLoad();if($null -ne $cfg){break};if($configAttempt -lt 6){Start-Sleep -Milliseconds 50}}
   if($null -eq $cfg){throw 'Candidate config unavailable after 6 attempts'}
   $result.LastStatusReadAttempts=$read.Attempts;$result.LastStatusReadElapsedMs=$read.ElapsedMs
   $result.StatusReadRetryTotal+=$read.Attempts-1
   if($cfg.BalancedThresholdSec -ne 300 -or $cfg.PowerSaverThresholdSec -ne 900){throw 'Default thresholds changed'}
   if(-not $status.isOnAC){throw 'AC supply changed'}
   if($status.paused){throw 'Candidate paused'}
   if([int]$status.enginePid -ne $owned[0].Id){throw 'Status PID changed'}
   if(([DateTimeOffset]::UtcNow-[DateTimeOffset]$status.timestamp).TotalSeconds -gt 90){throw 'Status stale'}
   $actual=(& powercfg.exe /getactivescheme)-join ' '
   if($actual -notmatch '[a-fA-F0-9]{8}(-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}'){throw 'Actual plan unavailable'}
   $actualGuid=$Matches[0]
   $owned[0].Refresh();$owned[1].Refresh()
   $sample=[pscustomobject]@{Utc=[DateTime]::UtcNow.ToString('O');ElapsedSec=$watch.Elapsed.TotalSeconds;ActualPlan=$actualGuid;Status=$status;StatusReadAttempts=$read.Attempts;StatusReadElapsedMs=$read.ElapsedMs;EngineCpuSec=$owned[0].TotalProcessorTime.TotalSeconds;TrayCpuSec=$owned[1].TotalProcessorTime.TotalSeconds;EnginePrivateBytes=$owned[0].PrivateMemorySize64;EngineHandles=$owned[0].HandleCount;EngineThreads=$owned[0].Threads.Count;TrayPrivateBytes=$owned[1].PrivateMemorySize64;TrayHandles=$owned[1].HandleCount;TrayThreads=$owned[1].Threads.Count}
   [IO.File]::AppendAllText($samplesFile,($sample|ConvertTo-Json -Depth 8 -Compress)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
   $sampleCount++;$result.SampleCount=$sampleCount;$result.LatestSample=$sample
   $recent=@(Get-Content (Join-Path $root 'SmartGuard.log') -Encoding UTF8 -Tail 100|ForEach-Object{try{$_|ConvertFrom-Json}catch{}})
   if(@($recent|Where-Object{$_.eventName -eq 'PowerSuspendObserved' -and [DateTimeOffset]$_.timestamp -ge [DateTimeOffset]$result.EpochUtc}).Count -gt 0){throw 'Sleep interrupted continuous awake observation'}
   $health=@($recent|Where-Object eventName -eq 'PipelineHealthSampled'|Select-Object -Last 1)
   if($health.Count -eq 1){$result.LatestHealth=$health[0];if($health[0].processId -ne $owned[0].Id){throw 'Health PID mismatch'};if($health[0].properties.droppedNormal -ne 0 -or $health[0].properties.droppedHigh -ne 0 -or $health[0].properties.failedBatches -ne 0){throw 'Log health failure or drop'}}
   if((Get-Item (Join-Path $root 'SmartGuard.log')).Length -gt 1048576){throw 'Evidence log exceeds single-file bound'}
   Save-Receipt
  }
  if($null -ne $soakAt -and $watch.Elapsed.TotalSeconds-$soakAt -ge $SoakMinutes*60){$result.Stage='collected';$result.Result='NeedsReview';break}
  Start-Sleep -Seconds 2
 }
 if($result.Stage -ne 'collected'){throw 'Eight-hour observation did not complete within bounded observation'}
 $log=Join-Path $root 'SmartGuard.log';if((Get-Item $log).Length -gt 1048576){throw 'Evidence log exceeds single-file bound'}
 $result.Health=@(Get-Content $log -Encoding UTF8|ForEach-Object{try{$_|ConvertFrom-Json}catch{}}|Where-Object eventName -eq 'PipelineHealthSampled')
}catch{$result.Error=$_.Exception.Message;$result.Result='Blocked';$result.Stage='blocked'}
finally{
 $result.ObservationElapsedSec=$watch.Elapsed.TotalSeconds
 try{
  Stop-SmartGuardProcessesWithoutElevation
  foreach($index in $indices){Write-AcIndex $index $index.Value}
  & powercfg.exe /setactive $plan;if($LASTEXITCODE -ne 0){throw 'Plan restore failed'}
  foreach($index in $indices){if([SoakPowerRead]::Read([guid]$index.Scheme,[guid]$index.Sub,[guid]$index.Setting)-ne $index.Value){throw 'AC setting restore mismatch'}}
  foreach($b in $brightness){$m=Get-CimInstance -Namespace root/WMI -ClassName WmiMonitorBrightnessMethods|Where-Object InstanceName -eq $b.InstanceName;if($m){Invoke-CimMethod -InputObject $m -MethodName WmiSetBrightness -Arguments @{Timeout=[uint32]0;Brightness=[byte]$b.CurrentBrightness}|Out-Null}}
  foreach($task in $tasks){if($task.Enabled){Enable-ScheduledTask -TaskName $task.Name|Out-Null}}
  Start-SmartGuardRestoredTasks -Root $priorRoot -Snapshot $snapshot
  $restore=[Diagnostics.Stopwatch]::StartNew();do{$after=Capture-SmartGuardScheduledTasks;if(($after|ConvertTo-Json -Depth 8 -Compress)-eq($snapshot|ConvertTo-Json -Depth 8 -Compress)){break};Start-Sleep -Milliseconds 100}while($restore.Elapsed.TotalSeconds -lt 20)
  $result.After=$after;if(($after|ConvertTo-Json -Depth 8 -Compress)-ne($snapshot|ConvertTo-Json -Depth 8 -Compress)){throw 'Task baseline restore mismatch'}
  if((Get-FileHash $configPath).Hash -ne $configHash){throw 'Production config changed'}
  $result.Restored=$true
 }catch{$result.Error+='; restore: '+$_.Exception.Message;$result.Result='Blocked'}
 $result.CompletedUtc=[DateTime]::UtcNow.ToString('O');Save-Receipt
}
if($result.Result -eq 'Blocked'){exit 2}
exit 0
