[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$Root,
  [ValidateSet('LidMouseBattery','LidMouseAC','ExternalPlanRunning')]
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

  $powerEvents = Get-WinEvent -FilterHashtable @{
    LogName = 'System'
    StartTime = $startedAt.LocalDateTime
  } -ErrorAction Stop | Where-Object {
    $_.ProviderName -in @(
      'Microsoft-Windows-Kernel-Power',
      'Microsoft-Windows-Power-Troubleshooter') -and
    $_.Id -in @(1,42,107,506,507)
  }

  if ($Scenario -in @('LidMouseBattery','LidMouseAC')) {
    $suspend = @($newLines | Where-Object eventName -eq 'PowerSuspendObserved' | Select-Object -Last 1)
    $resume = @($newLines | Where-Object eventName -eq 'PowerResumeObserved' | Select-Object -First 1)
    $suspendObservedAt = $(if ($suspend.Count -eq 1 -and $suspend[0].properties.lifecycleObservedAtUtc) {
      [DateTimeOffset]$suspend.properties.lifecycleObservedAtUtc
    })
    $resumeObservedAt = $(if ($resume.Count -eq 1 -and $resume[0].properties.lifecycleObservedAtUtc) {
      [DateTimeOffset]$resume.properties.lifecycleObservedAtUtc
    })

    $checks.Add([pscustomobject]@{
      name = 'lifecycle-events'
      status = $(if ($null -ne $suspendObservedAt -and
        $null -ne $resumeObservedAt -and $suspendObservedAt -lt $resumeObservedAt) {
          'Passed'
        } elseif ($null -eq $suspendObservedAt -or $null -eq $resumeObservedAt) {
          'Blocked'
        } else { 'Failed' })
    })
    $checks.Add([pscustomobject]@{
      name = 'windows-suspend-resume-events'
      status = $(if (@($powerEvents | Where-Object Id -in @(42,506)).Count -ge 1 -and
        @($powerEvents | Where-Object Id -in @(1,107,507)).Count -ge 1) {
          'Passed'
        } else { 'Failed' })
    })
    $checks.Add([pscustomobject]@{
      name = 'no-suspend-plan-write'
      status = $(if ($null -ne $suspendObservedAt -and
        $null -ne $resumeObservedAt -and
        @($newLines | Where-Object {
          $_.eventName -eq 'PowerPlanSwitched' -and
          [DateTimeOffset]$_.timestamp -gt $suspendObservedAt -and
          [DateTimeOffset]$_.timestamp -lt $resumeObservedAt
        }).Count -eq 0) { 'Passed' }
        elseif ($null -eq $suspendObservedAt -or $null -eq $resumeObservedAt) { 'Blocked' }
        else { 'Failed' })
    })
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
