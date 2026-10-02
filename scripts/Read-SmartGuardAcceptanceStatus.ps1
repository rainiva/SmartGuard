#Requires -Version 7.2
function Read-SmartGuardAcceptanceStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$StatusPath,
        [Parameter(Mandatory)][string]$BinDirectory,
        [Parameter(Mandatory)][ValidateRange(1,2147483647)][int]$ExpectedEnginePid
    )
    # Use the existing snapshot reader, including its ReadWrite|Delete sharing.
    if(-not ('SmartGuard.Configuration.StatusJsonReader' -as [type])) {
        [Reflection.Assembly]::LoadFrom((Join-Path $BinDirectory 'SmartGuard.Contracts.dll'))|Out-Null
        [Reflection.Assembly]::LoadFrom((Join-Path $BinDirectory 'SmartGuard.Configuration.dll'))|Out-Null
    }
    $watch=[Diagnostics.Stopwatch]::StartNew()
    for($attempt=1;$attempt -le 6;$attempt++) {
        if((Get-Item -LiteralPath $StatusPath -ErrorAction SilentlyContinue).Length -gt 65536){throw 'Acceptance status exceeds 64 KiB'}
        $status=[SmartGuard.Configuration.StatusJsonReader]::TryRead($StatusPath)
        if($null -ne $status) {
            if($status.enginePid -ne $ExpectedEnginePid){throw 'Acceptance status PID mismatch'}
            if($status.isOnAC -ne $true){throw 'Acceptance AC supply changed'}
            if($status.paused){throw 'Acceptance candidate paused'}
            $at=[DateTimeOffset]::MinValue
            if(-not [DateTimeOffset]::TryParse([string]$status.timestamp,[ref]$at)){throw 'Acceptance status timestamp invalid'}
            $age=([DateTimeOffset]::UtcNow-$at).TotalSeconds
            if($age -gt 90 -or $age -lt -5){throw 'Acceptance status stale or future dated'}
            return [pscustomobject]@{Status=$status;Attempts=$attempt;ElapsedMs=$watch.Elapsed.TotalMilliseconds}
        }
        if($attempt -lt 6){Start-Sleep -Milliseconds 50}
    }
    throw 'Acceptance status unavailable after 6 attempts; no cached evidence accepted'
}
