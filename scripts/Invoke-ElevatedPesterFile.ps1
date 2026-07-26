param(
    [Parameter(Mandatory)]
    [string]$TestPath,
    [Parameter(Mandatory)]
    [string]$ResultPath,
    [Parameter(Mandatory)]
    [string]$TranscriptPath,
    [Parameter(Mandatory)]
    [string]$Tier
)

$ErrorActionPreference = 'Stop'
$transcriptStarted = $false
$payload = [ordered]@{
    Result = 'Failed'
    ExitCode = 1
    PassedCount = 0
    FailedCount = 0
    SkippedCount = 0
    TotalCount = 0
    Error = $null
    CompletedAtUtc = $null
}

try {
    $transcriptDirectory = Split-Path -Parent $TranscriptPath
    if (-not [string]::IsNullOrWhiteSpace($transcriptDirectory)) {
        [System.IO.Directory]::CreateDirectory($transcriptDirectory) | Out-Null
    }
    Start-Transcript -Path $TranscriptPath -Force | Out-Null
    $transcriptStarted = $true

    if (-not (Test-Path -LiteralPath $TestPath -PathType Leaf)) {
        throw "Pester test file does not exist: $TestPath"
    }

    $env:SMARTGUARD_ELEVATED_RUN = '1'
    $env:SMARTGUARD_TEST_TIER = $Tier
    Import-Module Pester -MinimumVersion 5.0 -Force
    $pesterResult = Invoke-Pester -Path $TestPath -PassThru
    if ($null -eq $pesterResult) {
        throw 'Invoke-Pester returned no result.'
    }

    $payload.Result = $pesterResult.Result.ToString()
    $payload.PassedCount = [int]$pesterResult.PassedCount
    $payload.FailedCount = [int]$pesterResult.FailedCount
    $payload.SkippedCount = [int]$pesterResult.SkippedCount
    $payload.TotalCount = [int]$pesterResult.TotalCount

    $containerErrors = @(
        foreach ($container in @($pesterResult.Containers)) {
            foreach ($errorRecord in @($container.ErrorRecord)) {
                if ($null -eq $errorRecord) {
                    continue
                }

                $message = [string]$errorRecord.Exception.Message
                if ([string]::IsNullOrWhiteSpace($message)) {
                    $message = [string]$errorRecord
                }
                if (-not [string]::IsNullOrWhiteSpace($message)) {
                    $message
                }
            }
        }
    )

    if ($payload.Result -eq 'Passed' -and $payload.TotalCount -gt 0) {
        $payload.ExitCode = 0
    }
    elseif ($containerErrors.Count -gt 0) {
        $payload.Error = $containerErrors -join [Environment]::NewLine
    }
    elseif ($payload.TotalCount -eq 0) {
        $payload.Error = 'Invoke-Pester discovered zero tests.'
    }
    else {
        $payload.Error = "Invoke-Pester completed with Result=$($payload.Result)."
    }
}
catch {
    $payload.Result = 'Failed'
    $payload.ExitCode = 1
    $payload.Error = $_.Exception.Message
    Write-Error $_ -ErrorAction Continue
}
finally {
    if ($transcriptStarted) {
        try {
            Stop-Transcript | Out-Null
        }
        catch {
            $payload.Result = 'Failed'
            $payload.ExitCode = 1
            $payload.Error = if ([string]::IsNullOrWhiteSpace([string]$payload.Error)) {
                $_.Exception.Message
            }
            else {
                "$($payload.Error) Stop-Transcript failed: $($_.Exception.Message)"
            }
        }
    }

    $payload.CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
    $resultDirectory = Split-Path -Parent $ResultPath
    if (-not [string]::IsNullOrWhiteSpace($resultDirectory)) {
        [System.IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
    }
    $resultJson = [pscustomobject]$payload | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText(
        $ResultPath,
        $resultJson,
        [System.Text.UTF8Encoding]::new($false))
}

exit ([int]$payload.ExitCode)
