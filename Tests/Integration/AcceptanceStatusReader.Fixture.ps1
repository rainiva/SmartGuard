#Requires -Version 7.2
param([string]$ReaderPath,[string]$BinDirectory,[string]$Root,[string]$Case)
$ErrorActionPreference='Stop'
[IO.Directory]::CreateDirectory($Root)|Out-Null
$path=Join-Path $Root 'SmartGuard.status.json'
$row=@{timestamp=[DateTimeOffset]::UtcNow.ToString('O');enginePid=123;isOnAC=$true;paused=$false;currentPlanGUID='8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'}
if($Case-eq'Stale'){$row.timestamp=[DateTimeOffset]::UtcNow.AddSeconds(-91).ToString('O')}
if($Case-eq'Future'){$row.timestamp=[DateTimeOffset]::UtcNow.AddSeconds(30).ToString('O')}
if($Case-eq'WrongPid'){$row.enginePid=456}
if($Case-eq'DC'){$row.isOnAC=$false}
if($Case-eq'Paused'){$row.paused=$true}
$content=$row|ConvertTo-Json -Compress
if($Case-eq'Corrupt'){$content='{broken'}
if($Case-eq'Oversize'){$content=' ' * 65537}
[IO.File]::WriteAllText($path,$content,[Text.UTF8Encoding]::new($false))
$lock=$null
if($Case-eq'SharedWriter'){$lock=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)}
if($Case-in@('TemporaryLock','PermanentLock')){$lock=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
if($Case-eq'TemporaryLock'){
 Add-Type 'using System; using System.Threading; public static class ReleaseStatusLock { public static void Later(IDisposable item) { new Thread(()=>{Thread.Sleep(150);item.Dispose();}).Start(); } }'
 [ReleaseStatusLock]::Later($lock)
}
try{
 . $ReaderPath
 $watch=[Diagnostics.Stopwatch]::StartNew()
 try{
  $read=Read-SmartGuardAcceptanceStatus -StatusPath $path -BinDirectory $BinDirectory -ExpectedEnginePid 123
  if($Case-eq'AtomicPublish'){
   [Reflection.Assembly]::LoadFrom((Join-Path $BinDirectory 'SmartGuard.Engine.dll'))|Out-Null
   $publisher=[SmartGuard.Engine.Infrastructure.StatusPublisher]::new($path)
   Add-Type @'
using System; using System.Threading;
public static class PublishStatusBurst {
 public static Exception Error;
 public static Thread Start(object publisher, object payload) {
  var t=new Thread(()=>{try{for(int i=0;i<500;i++){payload.GetType().GetProperty("batteryPercent").SetValue(payload,i%2);publisher.GetType().GetMethod("Publish").Invoke(publisher,new[]{payload});Thread.Sleep(1);}}catch(Exception e){Error=e;}});
  t.Start();return t;
 }
}
'@
   $writer=[PublishStatusBurst]::Start($publisher,$read.Status)
   $maxReadMs=0.0
   try{for($i=0;$i-lt500;$i++){$read=Read-SmartGuardAcceptanceStatus -StatusPath $path -BinDirectory $BinDirectory -ExpectedEnginePid 123;$maxReadMs=[Math]::Max($maxReadMs,$read.ElapsedMs)}}
   finally{$writer.Join()}
   if([PublishStatusBurst]::Error){throw [PublishStatusBurst]::Error}
  }
  $out=@{Success=$true;Attempts=$read.Attempts;Pid=$read.Status.enginePid}
 }
 catch{$out=@{Success=$false;Error=$_.Exception.Message}}
 $out.TotalFixtureMs=$watch.Elapsed.TotalMilliseconds
 $out.ElapsedMs=if($Case-eq'AtomicPublish'){$maxReadMs}else{$watch.Elapsed.TotalMilliseconds}
 [IO.File]::WriteAllText((Join-Path $Root 'result.json'),($out|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
}finally{if($lock){$lock.Dispose()}}
