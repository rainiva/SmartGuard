function Test-IsProcessElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-WindowsPowerShellExePath {
    $command = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    return Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
}

function Test-SmartGuardElevatedPesterResult {
    param(
        [AllowNull()]
        [psobject]$Result
    )

    if ($null -eq $Result) {
        return $false
    }

    $requiredProperties = @(
        'Result',
        'ExitCode',
        'PassedCount',
        'FailedCount',
        'SkippedCount',
        'TotalCount',
        'Error',
        'CompletedAtUtc'
    )
    foreach ($property in $requiredProperties) {
        if ($Result.PSObject.Properties.Name -notcontains $property) {
            return $false
        }
    }

    $completedAt = [DateTimeOffset]::MinValue
    if ([string]::IsNullOrWhiteSpace([string]$Result.CompletedAtUtc) -or
        -not [DateTimeOffset]::TryParse(
            [string]$Result.CompletedAtUtc,
            [ref]$completedAt)) {
        return $false
    }

    try {
        return (
            [string]$Result.Result -eq 'Passed' -and
            [int]$Result.ExitCode -eq 0 -and
            [int]$Result.TotalCount -gt 0)
    }
    catch {
        return $false
    }
}

function New-SmartGuardElevatedPesterFailureResult {
    param(
        [Parameter(Mandatory)]
        [string]$Error,
        [AllowNull()]
        [psobject]$Source
    )

    $passedCount = 0
    $failedCount = 0
    $skippedCount = 0
    $totalCount = 0
    if ($null -ne $Source) {
        try { $passedCount = [int]$Source.PassedCount } catch { $passedCount = 0 }
        try { $failedCount = [int]$Source.FailedCount } catch { $failedCount = 0 }
        try { $skippedCount = [int]$Source.SkippedCount } catch { $skippedCount = 0 }
        try { $totalCount = [int]$Source.TotalCount } catch { $totalCount = 0 }
    }

    return [pscustomobject]@{
        Result = 'Failed'
        ExitCode = 1
        PassedCount = $passedCount
        FailedCount = $failedCount
        SkippedCount = $skippedCount
        TotalCount = $totalCount
        Error = $Error
        CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
}

function ConvertTo-SmartGuardQuotedProcessArgument {
    param([string]$Value)

    return '"{0}"' -f $Value.Replace('"', '\"')
}

function Invoke-PesterFileSingleElevation {
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,
        [Parameter(Mandatory)]
        [string]$TestPath,
        [Parameter(Mandatory)]
        [string]$ResultPath,
        [Parameter(Mandatory)]
        [string]$TranscriptPath,
        [Parameter(Mandatory)]
        [string]$Tier,
        [string]$PowerShellExe,
        [scriptblock]$ProcessStarter
    )

    if (-not (Test-Path -LiteralPath $TestPath -PathType Leaf)) {
        return New-SmartGuardElevatedPesterFailureResult `
            -Error "Pester test file does not exist: $TestPath"
    }

    $runner = Join-Path $RepoRoot 'scripts\Invoke-ElevatedPesterFile.ps1'
    if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) {
        return New-SmartGuardElevatedPesterFailureResult `
            -Error "Elevated Pester runner does not exist: $runner"
    }

    $resultDirectory = Split-Path -Parent $ResultPath
    if (-not [string]::IsNullOrWhiteSpace($resultDirectory)) {
        [System.IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
    }
    Remove-Item -LiteralPath $ResultPath -Force -ErrorAction SilentlyContinue

    if ([string]::IsNullOrWhiteSpace($PowerShellExe)) {
        $PowerShellExe = Get-WindowsPowerShellExePath
    }
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', (ConvertTo-SmartGuardQuotedProcessArgument -Value $runner),
        '-TestPath', (ConvertTo-SmartGuardQuotedProcessArgument -Value $TestPath),
        '-ResultPath', (ConvertTo-SmartGuardQuotedProcessArgument -Value $ResultPath),
        '-TranscriptPath', (ConvertTo-SmartGuardQuotedProcessArgument -Value $TranscriptPath),
        '-Tier', (ConvertTo-SmartGuardQuotedProcessArgument -Value $Tier)
    )

    try {
        if ($null -ne $ProcessStarter) {
            $process = & $ProcessStarter $PowerShellExe $arguments
        }
        elseif (Test-IsProcessElevated) {
            $process = Start-Process `
                -FilePath $PowerShellExe `
                -PassThru `
                -ArgumentList $arguments
        }
        else {
            $process = Start-Process `
                -FilePath $PowerShellExe `
                -Verb RunAs `
                -PassThru `
                -ArgumentList $arguments
        }
    }
    catch {
        return New-SmartGuardElevatedPesterFailureResult -Error (
            "Unable to launch elevated Windows PowerShell ($PowerShellExe): " +
            $_.Exception.Message)
    }

    if ($null -eq $process) {
        return New-SmartGuardElevatedPesterFailureResult -Error (
            "Unable to launch elevated Windows PowerShell ($PowerShellExe): " +
            'the process launch returned no process.')
    }

    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    while ([DateTime]::UtcNow -lt $deadline) {
        $processHasExited = $false
        try {
            $processHasExited = [bool]$process.HasExited
        }
        catch {
            $processHasExited = $false
        }

        if (Test-Path -LiteralPath $ResultPath -PathType Leaf) {
            $diskResult = $null
            try {
                $resultJson = [System.IO.File]::ReadAllText(
                    $ResultPath,
                    [System.Text.Encoding]::UTF8)
                $diskResult = $resultJson | ConvertFrom-Json -ErrorAction Stop
            }
            catch {
                if ($processHasExited) {
                    return New-SmartGuardElevatedPesterFailureResult -Error (
                        "Elevated Pester result JSON is invalid: $($_.Exception.Message)")
                }
            }

            if ($null -ne $diskResult) {
                if (-not (Test-SmartGuardElevatedPesterResult -Result $diskResult)) {
                    if ($processHasExited) {
                        $failureError = [string]$diskResult.Error
                        if ([string]::IsNullOrWhiteSpace($failureError)) {
                            $failureError =
                                'Elevated Pester result failed validation.'
                        }
                        return New-SmartGuardElevatedPesterFailureResult `
                            -Error $failureError `
                            -Source $diskResult
                    }
                }
                elseif ($processHasExited) {
                    try {
                        if ([int]$process.ExitCode -ne [int]$diskResult.ExitCode) {
                            return New-SmartGuardElevatedPesterFailureResult `
                                -Error 'Elevated process exit code did not match result JSON.' `
                                -Source $diskResult
                        }
                    }
                    catch {
                        return New-SmartGuardElevatedPesterFailureResult `
                            -Error "Unable to read elevated process exit code: $($_.Exception.Message)" `
                            -Source $diskResult
                    }

                    return $diskResult
                }
            }
        }
        elseif ($processHasExited) {
            $childExitCode = 'unknown'
            try { $childExitCode = [int]$process.ExitCode } catch { }
            return New-SmartGuardElevatedPesterFailureResult -Error (
                "Elevated Pester process exited with code $childExitCode " +
                'without publishing result JSON.')
        }

        Start-Sleep -Milliseconds 250
    }

    return New-SmartGuardElevatedPesterFailureResult -Error (
        'Timed out after 10 minutes waiting for elevated Pester result JSON.')
}

function Invoke-InstallerUserFlowSingleElevation {
    param(
        [string]$RepoRoot,
        [string]$TestScriptPath
    )

    if ($env:SMARTGUARD_SKIP_INSTALLER_TESTS -eq '1') {
        return [PSCustomObject]@{
            HandedOff  = $false
            ExitCode   = 0
            ShouldSkip = $true
            SkipReason = 'SMARTGUARD_SKIP_INSTALLER_TESTS=1'
        }
    }

    if ((Test-IsProcessElevated) -or $env:SMARTGUARD_ELEVATED_RUN -eq '1') {
        return [PSCustomObject]@{
            HandedOff  = $false
            ExitCode   = 0
            ShouldSkip = $false
            SkipReason = $null
            PesterResult = $null
        }
    }

    Write-Host 'SmartGuard: requesting one-time UAC elevation for installer integration tests...' -ForegroundColor Cyan
    $transcript = Join-Path $RepoRoot '.work\test-results\installer-user-flow-elevated.transcript.log'
    $resultPath = Join-Path $RepoRoot '.work\test-results\installer-user-flow-elevated.result.json'
    $powershellExe = Get-WindowsPowerShellExePath
    $startElevatedProcess = {
        param(
            [string]$ResolvedPowerShellExe,
            [object[]]$ResolvedArgumentList
        )

        $powershellExe = $ResolvedPowerShellExe
        try {
            Start-Process -FilePath $powershellExe -Verb RunAs -PassThru -ArgumentList $ResolvedArgumentList
        }
        catch {
            throw "Unable to launch elevated Windows PowerShell ($powershellExe): $($_.Exception.Message)"
        }
    }

    $pesterResult = Invoke-PesterFileSingleElevation `
        -RepoRoot $RepoRoot `
        -TestPath $TestScriptPath `
        -ResultPath $resultPath `
        -TranscriptPath $transcript `
        -Tier full `
        -PowerShellExe $powershellExe `
        -ProcessStarter $startElevatedProcess

    return [PSCustomObject]@{
        HandedOff  = $true
        ExitCode   = [int]$pesterResult.ExitCode
        ShouldSkip = $false
        SkipReason = $null
        PesterResult = $pesterResult
    }
}
