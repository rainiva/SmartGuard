param([string]$ScriptPath, [string]$Root, [string]$Case)
$encoding = [Text.UTF8Encoding]::new($false)
[IO.Directory]::CreateDirectory($Root) | Out-Null
function Health {
    param([bool]$After)
    $row = @{timestamp=[DateTimeOffset]::UtcNow.ToString('O'); eventName='PipelineHealthSampled'; processId=123; properties=@{droppedNormal=0; droppedHigh=0; failedBatches=0}}
    if ($Case -eq 'MissingPid') { $row.Remove('processId') }
    if ($After -and $Case -eq 'Dropped') { $row.properties.droppedNormal=1 }
    return ($row | ConvertTo-Json -Depth 6 -Compress)
}
$logPath = Join-Path $Root 'SmartGuard.log'
[IO.File]::WriteAllText($logPath, $(if ($Case -eq 'MissingBaseline') { '' } else { (Health $false) + "`n" }), $encoding)
function Read-Host {
    param($Prompt)
    $rows = [Collections.Generic.List[string]]::new()
    if ($Case -ne 'MissingLifecycle') {
        foreach ($name in @('PowerSuspendObserved','PowerResumeObserved')) {
            $rows.Add((@{timestamp=[DateTimeOffset]::UtcNow.ToString('O'); eventName=$name; properties=@{lifecycleObservedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}} | ConvertTo-Json -Depth 6 -Compress))
            Start-Sleep -Milliseconds 5
        }
    }
    if ($Case -ne 'MissingPost') { $rows.Add((Health $true)) }
    [IO.File]::AppendAllText($logPath, ($rows -join "`n") + "`n", $encoding)
    [IO.File]::WriteAllText((Join-Path $Root 'SmartGuard.status.json'), (@{timestamp=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json), $encoding)
    return ''
}
function Get-WinEvent {
    param($FilterHashtable, $FilterXPath, $LogName, $ErrorAction)
    if ($Case -eq 'NarrowQuery' -and ($LogName -ne 'System' -or $FilterXPath -notmatch 'TimeCreated\[@SystemTime' -or $FilterXPath -notmatch 'Microsoft-Windows-Kernel-Power' -or $FilterXPath -notmatch 'EventID=506')) { throw 'Acceptance must query the UTC power-event window at source' }
    @([pscustomobject]@{ProviderName='Microsoft-Windows-Kernel-Power'; Id=506}, [pscustomobject]@{ProviderName='Microsoft-Windows-Kernel-Power'; Id=507})
}
& $ScriptPath -Root $Root -Scenario LidMouseAC -ResultPath (Join-Path $Root 'result.json') -TranscriptPath (Join-Path $Root 'transcript.txt')
exit $LASTEXITCODE
