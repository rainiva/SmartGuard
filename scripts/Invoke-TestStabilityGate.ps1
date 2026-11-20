param(
    [string]$Project,
    [string]$Filter,
    [int]$Iterations = 0,
    [string]$ResultPath,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Invoke-SmartGuardDotNetTests.ps1')

function Invoke-SmartGuardStabilityIteration {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Project,

        [string]$Filter,

        [Parameter(Mandatory = $true)]
        [string]$ResultDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RunId,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [int]::MaxValue)]
        [int]$Iteration,

        [int]$TimeoutSeconds = 900
    )

    $arguments = @('test', $Project, '--nologo', '-v', 'q')
    if ($Filter) {
        $arguments += @('--filter', $Filter)
    }

    $iterationRunId = '{0}-i{1:D3}' -f $RunId, $Iteration
    $trxFileName = "$iterationRunId.trx"
    $trxPath = Join-Path $ResultDirectory $trxFileName
    $arguments += @(
        '--logger',
        "trx;LogFileName=$trxFileName",
        '--results-directory',
        $ResultDirectory
    )
    $argumentText = ($arguments | ForEach-Object {
        ConvertTo-SmartGuardCmdArgument -Value $_
    }) -join ' '

    $entry = Start-SmartGuardCapturedCommand `
        -CommandText "dotnet $argumentText" `
        -ResultDirectory $ResultDirectory `
        -RunId $iterationRunId
    $capture = Complete-SmartGuardCapturedCommand -Entry $entry -TimeoutSeconds $TimeoutSeconds
    $trxResult = Read-SmartGuardTrxResult -Path $trxPath -ExitCode $capture.ExitCode

    $iterationErrors = @()
    if ($capture.TimedOut) {
        $iterationErrors += "iteration $Iteration timed out after $TimeoutSeconds seconds"
    }
    if ($trxResult.Result -ne 'Passed') {
        $iterationErrors += $trxResult.Error
    }
    $iterationErrors = @($iterationErrors | Where-Object { $_ -ne '' })

    return [pscustomobject]@{
        Result = if ($iterationErrors.Count -eq 0) { 'Passed' } else { 'Failed' }
        ExitCode = $capture.ExitCode
        TrxPath = $trxPath
        Output = $capture.Output
        Error = $iterationErrors -join '; '
    }
}

function Invoke-SmartGuardTestStabilityGate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Project,

        [string]$Filter,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [int]::MaxValue)]
        [int]$Iterations,

        [Parameter(Mandatory = $true)]
        [string]$ResultPath,

        [int]$TimeoutSeconds = 900
    )

    $resultDirectory = Split-Path -Parent $ResultPath
    if ([string]::IsNullOrWhiteSpace($resultDirectory)) {
        $resultDirectory = '.'
    }
    [void](New-Item -ItemType Directory -Path $resultDirectory -Force)

    $runId = 'stability-' + [guid]::NewGuid().ToString('N')
    $completed = 0
    $failures = 0
    $firstFailure = $null
    $trxPaths = @()

    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        $iterationResult = Invoke-SmartGuardStabilityIteration `
            -Project $Project `
            -Filter $Filter `
            -ResultDirectory $resultDirectory `
            -RunId $runId `
            -Iteration $iteration `
            -TimeoutSeconds $TimeoutSeconds
        $completed = $iteration
        $trxPaths += $iterationResult.TrxPath

        if ($iterationResult.Result -ne 'Passed') {
            # 任一轮失败立即停止；禁止重跑失败轮把第一失败证据洗成绿。
            $failures = 1
            $firstFailure = [pscustomobject]@{
                Iteration = $iteration
                ExitCode = $iterationResult.ExitCode
                TrxPath = $iterationResult.TrxPath
                Output = $iterationResult.Output
                Error = $iterationResult.Error
            }
            break
        }
    }

    $gateResult = [pscustomobject]@{
        Result = if ($failures -eq 0 -and $completed -eq $Iterations) { 'Passed' } else { 'Failed' }
        Project = $Project
        Filter = $Filter
        Iterations = $Iterations
        Completed = $completed
        Failures = $failures
        FirstFailure = $firstFailure
        TrxPaths = $trxPaths
        CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
    }

    $json = $gateResult | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText(
        $ResultPath,
        $json,
        [System.Text.UTF8Encoding]::new($false))

    return $gateResult
}

if ($MyInvocation.InvocationName -ne '.') {
    if ([string]::IsNullOrWhiteSpace($Project)) {
        throw 'Project is required.'
    }
    if ($Iterations -lt 1) {
        throw 'Iterations must be at least 1.'
    }
    if ([string]::IsNullOrWhiteSpace($ResultPath)) {
        throw 'ResultPath is required.'
    }

    $gate = Invoke-SmartGuardTestStabilityGate `
        -Project $Project `
        -Filter $Filter `
        -Iterations $Iterations `
        -ResultPath $ResultPath `
        -TimeoutSeconds $TimeoutSeconds

    Write-Host "STABILITY: Result=$($gate.Result) Completed=$($gate.Completed)/$($gate.Iterations) Failures=$($gate.Failures)"
    if ($null -ne $gate.FirstFailure) {
        Write-Host "FIRST FAILURE: iteration=$($gate.FirstFailure.Iteration) exit=$($gate.FirstFailure.ExitCode) trx=$($gate.FirstFailure.TrxPath)" -ForegroundColor Red
        if ($gate.FirstFailure.Output) {
            Write-Host $gate.FirstFailure.Output.TrimEnd()
        }
        if ($gate.FirstFailure.Error) {
            Write-Host $gate.FirstFailure.Error -ForegroundColor Red
        }
    }

    exit ([int]($gate.Result -ne 'Passed'))
}
