param(
    [Parameter(Mandatory)]
    [string]$RepoRoot,
    [Parameter(Mandatory)]
    [string]$TestScriptPath,
    [Parameter(Mandatory)]
    [string]$DeclinedMarkerPath,
    [Parameter(Mandatory)]
    [string]$TranscriptPath,
    [Parameter(Mandatory)]
    [string]$ResultPath
)

$ErrorActionPreference = 'Stop'
$transcriptStarted = $false
$exitCode = 1
$errorMessage = $null
try {
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $TranscriptPath)) | Out-Null
    Start-Transcript -Path $TranscriptPath -Force | Out-Null
    $transcriptStarted = $true

    Set-Location -LiteralPath $RepoRoot
    $env:SMARTGUARD_ELEVATED_RUN = '1'
    Remove-Item -LiteralPath $DeclinedMarkerPath -Force -ErrorAction SilentlyContinue

    Import-Module Pester -MinimumVersion 5.0 -Force
    $result = Invoke-Pester -Path $TestScriptPath -PassThru
    if ($null -eq $result -or $result.Result -ne 'Passed') {
        $errorMessage = 'Installer integration Pester run did not pass.'
    }
    else {
        $exitCode = 0
    }
}
catch {
    $errorMessage = $_.Exception.Message
    Write-Error $_
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }

    $resultDirectory = Split-Path -Parent $ResultPath
    [System.IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
    $resultPayload = [PSCustomObject]@{
        ExitCode = $exitCode
        Error = $errorMessage
        CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
    } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText(
        $ResultPath,
        $resultPayload,
        [System.Text.UTF8Encoding]::new($false))
}

exit $exitCode
