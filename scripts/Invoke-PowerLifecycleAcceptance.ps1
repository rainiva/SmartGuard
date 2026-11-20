[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$Root,
  [ValidateSet('LidMouseBattery','LidMouseAC','HibernateResume','ExternalPlanRunning')]
  [string]$Scenario,
  [string]$ResultPath = '.work/test-results/power-lifecycle-acceptance.result.json',
  [string]$TranscriptPath = '.work/test-results/power-lifecycle-acceptance.transcript.txt'
)

$ErrorActionPreference = 'Stop'
$startedAt = [DateTimeOffset]::Now
$checks = [System.Collections.Generic.List[object]]::new()
$resultFullPath = [System.IO.Path]::GetFullPath($ResultPath)
$transcriptFullPath = [System.IO.Path]::GetFullPath($TranscriptPath)
[System.IO.Directory]::CreateDirectory(
  [System.IO.Path]::GetDirectoryName($resultFullPath)) | Out-Null
[System.IO.Directory]::CreateDirectory(
  [System.IO.Path]::GetDirectoryName($transcriptFullPath)) | Out-Null
Start-Transcript -LiteralPath $transcriptFullPath -Force | Out-Null

function Write-Result {
  param(
    [Parameter(Mandatory)] [string]$State,
    [object]$ErrorMessage
  )

  $result = [pscustomobject]@{
    schemaVersion = 1
    scenario = $Scenario
    startedAt = $startedAt.ToString('O')
    completedAt = [DateTimeOffset]::Now.ToString('O')
    result = $State
    checks = $checks
    error = $ErrorMessage
  }
  [System.IO.File]::WriteAllText(
    $resultFullPath,
    ($result | ConvertTo-Json -Depth 8),
    [System.Text.UTF8Encoding]::new($false))
  return $State
}

try {
  $logPath = Join-Path $Root 'SmartGuard.log'
  $statusPath = Join-Path $Root 'SmartGuard.status.json'
  $baselineHealth = @(Get-Content -LiteralPath $logPath -Encoding utf8 -Tail 2000 | ForEach-Object { try { $_ | ConvertFrom-Json } catch { $null } } | Where-Object eventName -eq 'PipelineHealthSampled' | Select-Object -Last 1)
  $initialLength = (Get-Item -LiteralPath $logPath).Length

  Write-Host "Starting scenario $Scenario. The script will not sleep or change power plans."
  Read-Host 'Complete the manual operation, keep the machine awake, wait for the next LogPipeline health sample, then press Enter' | Out-Null

  if ((Get-Item -LiteralPath $logPath).Length -lt $initialLength) {
    throw 'The main log rotated during acceptance; rerun this scenario.'
  }

  $stream = [System.IO.File]::Open(
    $logPath,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::Read,
    [System.IO.FileShare]::ReadWrite)
  $stream.Seek($initialLength, [System.IO.SeekOrigin]::Begin) | Out-Null
  $reader = [System.IO.StreamReader]::new(
    $stream, [System.Text.UTF8Encoding]::new($false, $true), $false)
  try { $appendedText = $reader.ReadToEnd() }
  finally { $reader.Dispose(); $stream.Dispose() }

  $newLines = @([regex]::Split($appendedText, '\r?\n') | ForEach-Object {
      try { $_ | ConvertFrom-Json } catch { $null }
    } | Where-Object {
      $_ -and [DateTimeOffset]$_.timestamp -ge $startedAt.ToUniversalTime()
    })
  $postHealth = @($newLines | Where-Object eventName -eq 'PipelineHealthSampled' | Select-Object -Last 1)

  $status = [System.IO.File]::ReadAllText($statusPath, [System.Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json

  $fromUtc = $startedAt.UtcDateTime.ToString('O')
  $toUtc = [DateTimeOffset]::UtcNow.UtcDateTime.ToString('O')
  $powerXPath = "*[System[((Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=42 or EventID=107 or EventID=506 or EventID=507)) or (Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1)) and TimeCreated[@SystemTime >= '$fromUtc' and @SystemTime <= '$toUtc']]]"
  $powerEvents = Get-WinEvent -LogName System -FilterXPath $powerXPath -ErrorAction Stop

  if ($Scenario -in @('LidMouseBattery','LidMouseAC','HibernateResume')) {
    $intervals = [System.Collections.Generic.List[object]]::new()
    $pendingSuspend = $null
    $lifecycleState = 'Passed'
    foreach ($event in @($newLines | Where-Object eventName -in @('PowerSuspendObserved','PowerResumeObserved'))) {
      if (-not $event.properties.lifecycleObservedAtUtc) { $lifecycleState = 'Blocked'; continue }
      $observed = [DateTimeOffset]$event.properties.lifecycleObservedAtUtc
      if ($event.eventName -eq 'PowerSuspendObserved') {
        if ($null -eq $pendingSuspend) { $pendingSuspend = $observed }
      }
      elseif ($null -ne $pendingSuspend) {
        if ($observed -le $pendingSuspend) { $lifecycleState = 'Failed' }
        $intervals.Add([pscustomobject]@{ Suspend = $pendingSuspend; Resume = $observed })
        $pendingSuspend = $null
      }
      else { $lifecycleState = 'Blocked' }
    }
    if ($intervals.Count -eq 0 -or $null -ne $pendingSuspend) { $lifecycleState = 'Blocked' }

    $checks.Add([pscustomobject]@{
      name = 'lifecycle-events'
      status = $lifecycleState
    })
    $checks.Add([pscustomobject]@{
      name = 'windows-suspend-resume-events'
      status = $(if (@($powerEvents | Where-Object Id -in @(42,506)).Count -ge 1 -and
        @($powerEvents | Where-Object Id -in @(1,107,507)).Count -ge 1) {
          'Passed'
        } else { 'Failed' })
    })
    $suspendWrites = @($newLines | Where-Object {
      if ($_.eventName -ne 'PowerPlanSwitched') { return $false }
      $writtenAt = [DateTimeOffset]$_.timestamp
      @($intervals | Where-Object { $writtenAt -gt $_.Suspend -and $writtenAt -lt $_.Resume }).Count -gt 0
    })
    $checks.Add([pscustomobject]@{
      name = 'no-suspend-plan-write'
      status = $(if ($suspendWrites.Count -gt 0) { 'Failed' }
        elseif ($lifecycleState -ne 'Passed') { $lifecycleState }
        else { 'Passed' })
    })
    if ($Scenario -eq 'HibernateResume') {
      $hibernate = @($powerEvents | Where-Object Id -eq 42 | Select-Object -First 1)
      $hibernateState = 'Blocked'
      if ($hibernate.Count -eq 1) {
        $xml = [xml]$hibernate[0].ToXml()
        $target = @($xml.Event.EventData.Data | Where-Object Name -eq 'TargetState' | Select-Object -First 1)
        $effective = @($xml.Event.EventData.Data | Where-Object Name -eq 'EffectiveState' | Select-Object -First 1)
        if ($target.Count -eq 1 -and $effective.Count -eq 1 -and $target[0].InnerText -and $effective[0].InnerText) {
          $hibernateState = if ($target[0].InnerText -eq '5' -and $effective[0].InnerText -eq '5') { 'Passed' } else { 'Failed' }
        }
      }
      $checks.Add([pscustomobject]@{ name = 'hibernate-state'; status = $hibernateState })
    }
  }
  else {
    $checks.Add([pscustomobject]@{
      name = 'external-change-observed-once'
      status = $(if (@($newLines | Where-Object eventName -eq 'ExternalPlanChange').Count -eq 1) {
          'Passed'
        } else { 'Failed' })
    })
  }

  $checks.Add([pscustomobject]@{
    name = 'status-freshness'
    status = $(if (([DateTimeOffset]::Now - [DateTimeOffset]$status.timestamp) -le
      [TimeSpan]::FromSeconds(90)) { 'Passed' } else { 'Failed' })
  })

  $pipelineStatus = if ($baselineHealth.Count -ne 1 -or $postHealth.Count -ne 1 -or
    [int]$baselineHealth.processId -le 0 -or [int]$postHealth.processId -le 0 -or
    [int]$baselineHealth.processId -ne [int]$postHealth.processId) {
    'Blocked'
  }
  elseif (
    [long]$postHealth.properties.droppedNormal -eq [long]$baselineHealth.properties.droppedNormal -and
    [long]$postHealth.properties.droppedHigh -eq [long]$baselineHealth.properties.droppedHigh -and
    [long]$postHealth.properties.failedBatches -eq [long]$baselineHealth.properties.failedBatches) {
    'Passed'
  }
  else { 'Failed' }
  $checks.Add([pscustomobject]@{ name = 'pipeline-health-delta'; status = $pipelineStatus })

  $resultState = if (@($checks | Where-Object status -eq 'Blocked').Count -gt 0) {
    'Blocked'
  }
  elseif (@($checks | Where-Object status -eq 'Failed').Count -gt 0) {
    'Failed'
  }
  else { 'Passed' }

  [void](Write-Result -State $resultState)
  if ($resultState -eq 'Blocked') { exit 2 }
  if ($resultState -eq 'Failed') { exit 1 }
}
catch {
  $errorResult = [pscustomobject]@{
    schemaVersion = 1
    scenario = $Scenario
    startedAt = $startedAt.ToString('O')
    completedAt = [DateTimeOffset]::Now.ToString('O')
    result = 'Blocked'
    checks = $checks
    error = $_.Exception.Message
  }
  [System.IO.File]::WriteAllText(
    $resultFullPath,
    ($errorResult | ConvertTo-Json -Depth 8),
    [System.Text.UTF8Encoding]::new($false))
  exit 2
}
finally {
  try { Stop-Transcript | Out-Null } catch { }
}
