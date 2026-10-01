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
    if ($Case -in @('RepeatedLifecycle','RepeatedLifecycleWrite','UnclosedLifecycle')) {
        $at = [DateTimeOffset]::UtcNow
        $events = @('PowerSuspendObserved','PowerResumeObserved','PowerSuspendObserved','PowerResumeObserved')
        if ($Case -eq 'UnclosedLifecycle') { $events = $events[0..2] }
        for ($i=0; $i -lt $events.Count; $i++) {
            $observed = $at.AddMilliseconds($i * 10)
            $rows.Add((@{timestamp=[DateTimeOffset]::UtcNow.ToString('O'); eventName=$events[$i]; properties=@{lifecycleObservedAtUtc=$observed.ToString('O')}} | ConvertTo-Json -Depth 6 -Compress))
            if ($i -eq 0 -and $Case -eq 'RepeatedLifecycleWrite') {
                $rows.Add((@{timestamp=$observed.AddMilliseconds(5).ToString('O'); eventName='PowerPlanSwitched'} | ConvertTo-Json -Compress))
            }
        }
    }
    elseif ($Case -ne 'MissingLifecycle') {
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
    if ($Case -like 'Hibernate*') {
        $state = if ($Case -eq 'HibernateWrongState') { '4' } elseif ($Case -eq 'HibernateMissingState') { '' } else { '5' }
        $event = [pscustomobject]@{ProviderName='Microsoft-Windows-Kernel-Power'; Id=42; State=$state}
        $event | Add-Member -MemberType ScriptMethod -Name ToXml -Value { '<Event><EventData><Data Name="TargetState">'+$this.State+'</Data><Data Name="EffectiveState">'+$this.State+'</Data></EventData></Event>' }
        return @($event, [pscustomobject]@{ProviderName='Microsoft-Windows-Kernel-Power'; Id=107})
    }
    @([pscustomobject]@{ProviderName='Microsoft-Windows-Kernel-Power'; Id=506}, [pscustomobject]@{ProviderName='Microsoft-Windows-Kernel-Power'; Id=507})
}
$scenario = if ($Case -like 'Hibernate*') { 'HibernateResume' } else { 'LidMouseAC' }
& $ScriptPath -Root $Root -Scenario $scenario -ResultPath (Join-Path $Root 'result.json') -TranscriptPath (Join-Path $Root 'transcript.txt')
exit $LASTEXITCODE
