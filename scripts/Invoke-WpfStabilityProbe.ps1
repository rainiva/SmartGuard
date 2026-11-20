[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('test-contamination', 'production-equivalent')]
    [string]$Mode,

    [ValidateRange(1, 10000)]
    [int]$Iterations = 1,

    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 60,

    [int]$Seed = 20260823,

    [string]$Filter = 'FullyQualifiedName~WpfStaTestHostTests',

    [AllowEmptyString()]
    [string]$ToolRoot = '',

    [switch]$RequireReproduction
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ToolRoot)) {
    $ToolRoot = Join-Path $PSScriptRoot '..\.work\diagnostics\dotnet-tools'
}

function ConvertTo-WpfStabilityProbeCmdArgument {
    param([Parameter(Mandatory = $true)][string]$Value)

    '"{0}"' -f $Value.Replace('"', '""')
}

function Resolve-WpfStabilityTestHostPid {
    param(
        [Parameter(Mandatory = $true)][int]$RootProcessId,
        [Parameter(Mandatory = $true)][DateTime]$StartedUtc
    )

    try {
        $processIds = @([SmartGuardCapturedProcessTreeSnapshot]::GetProcessIds($RootProcessId))
    }
    catch {
        return $null
    }

    foreach ($processId in $processIds) {
        if ($processId -eq $RootProcessId) {
            continue
        }
        try {
            $process = Get-Process -Id $processId -ErrorAction Stop
            if ($process.ProcessName -notmatch '^testhost(\.x86)?$') {
                continue
            }
            if ($process.StartTime.ToUniversalTime() -lt $StartedUtc) {
                continue
            }
            return $process.Id
        }
        catch {
            continue
        }
    }
    return $null
}

function Start-WpfStabilityDiagnosticCapture {
    param(
        [Parameter(Mandatory = $true)][string]$ToolRoot,
        [Parameter(Mandatory = $true)][string]$RunDirectory,
        [Nullable[int]]$TestHostPid,
        [string]$ArtifactPrefix = '',
        [ValidateSet('live', 'timeout-pre-terminate')]
        [string]$CapturePhase = 'live',
        [Nullable[DateTime]]$CaptureDeadlineUtc
    )

    $definitions = @(
        [pscustomobject]@{
            Tool = 'dotnet-counters'
            FileName = 'dotnet-counters.exe'
            Arguments = { param($targetProcessId, $artifact) @('collect', '--process-id', $targetProcessId, '--format', 'json', '--output', $artifact, '--duration', '00:00:01') }
            ArtifactName = 'counters.json'
        }
        [pscustomobject]@{
            Tool = 'dotnet-stack'
            FileName = 'dotnet-stack.exe'
            Arguments = { param($targetProcessId, $artifact) @('report', '--process-id', $targetProcessId) }
            ArtifactName = $null
        }
        [pscustomobject]@{
            Tool = 'dotnet-trace'
            FileName = 'dotnet-trace.exe'
            Arguments = { param($targetProcessId, $artifact) @('collect', '--process-id', $targetProcessId, '--duration', '00:00:00.250', '--output', $artifact) }
            ArtifactName = 'trace.nettrace'
        }
    )

    $entries = @()
    foreach ($definition in $definitions) {
        $toolPath = Join-Path $ToolRoot $definition.FileName
        $stdoutPath = Join-Path $RunDirectory ($ArtifactPrefix + $definition.Tool + '.stdout.log')
        $stderrPath = Join-Path $RunDirectory ($ArtifactPrefix + $definition.Tool + '.stderr.log')
        $artifactPath = if ($null -eq $definition.ArtifactName) {
            $stdoutPath
        }
        else {
            Join-Path $RunDirectory ($ArtifactPrefix + $definition.ArtifactName)
        }
        if ($null -ne $CaptureDeadlineUtc -and [DateTime]::UtcNow -ge $CaptureDeadlineUtc) {
            $entries += [pscustomobject]@{
                Tool = $definition.Tool; ToolPath = $toolPath; Status = 'Blocked';
                Reason = 'The bounded timeout-capture handoff deadline was reached before this tool could start.';
                Command = $null; ExitCode = $null; StdoutPath = $stdoutPath; StderrPath = $stderrPath;
                ArtifactPath = $artifactPath; CapturePhase = $CapturePhase; CaptureDeadlineUtc = $CaptureDeadlineUtc; Process = $null
            }
            continue
        }
        if (-not (Test-Path -LiteralPath $toolPath)) {
            $entries += [pscustomobject]@{
                Tool = $definition.Tool; ToolPath = $toolPath; Status = 'Unavailable';
                Reason = "Diagnostic tool is missing from ToolRoot: $toolPath";
                Command = $null; ExitCode = $null; StdoutPath = $stdoutPath; StderrPath = $stderrPath;
                ArtifactPath = $artifactPath; CapturePhase = $CapturePhase; CaptureDeadlineUtc = $CaptureDeadlineUtc; Process = $null
            }
            continue
        }
        if ($null -eq $TestHostPid) {
            $entries += [pscustomobject]@{
                Tool = $definition.Tool; ToolPath = $toolPath; Status = 'Blocked';
                Reason = 'The actual testhost PID was not resolved while the controller child tree was live.';
                Command = $null; ExitCode = $null; StdoutPath = $stdoutPath; StderrPath = $stderrPath;
                ArtifactPath = $artifactPath; CapturePhase = $CapturePhase; CaptureDeadlineUtc = $CaptureDeadlineUtc; Process = $null
            }
            continue
        }

        $arguments = @(& $definition.Arguments ([int]$TestHostPid) $artifactPath)
        try {
            $startInfo = New-Object System.Diagnostics.ProcessStartInfo
            $startInfo.FileName = $toolPath
            $startInfo.Arguments = (($arguments | ForEach-Object {
                ConvertTo-WpfStabilityProbeCmdArgument -Value ([string]$_)
            }) -join ' ')
            $startInfo.WorkingDirectory = $RunDirectory
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $process = New-Object System.Diagnostics.Process
            $process.StartInfo = $startInfo
            if (-not $process.Start()) {
                throw "Unable to start diagnostic tool: $toolPath"
            }
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            $entries += [pscustomobject]@{
                Tool = $definition.Tool; ToolPath = $toolPath; Status = 'Started'; Reason = '';
                Command = ('"{0}" {1}' -f $toolPath, ($arguments -join ' ')); ExitCode = $null;
                StdoutPath = $stdoutPath; StderrPath = $stderrPath; ArtifactPath = $artifactPath;
                CapturePhase = $CapturePhase; CaptureDeadlineUtc = $CaptureDeadlineUtc; Process = $process;
                StdoutTask = $stdoutTask; StderrTask = $stderrTask
            }
        }
        catch {
            $entries += [pscustomobject]@{
                Tool = $definition.Tool; ToolPath = $toolPath; Status = 'Failed'; Reason = $_.Exception.Message;
                Command = ('"{0}" {1}' -f $toolPath, ($arguments -join ' ')); ExitCode = $null;
                StdoutPath = $stdoutPath; StderrPath = $stderrPath; ArtifactPath = $artifactPath;
                CapturePhase = $CapturePhase; CaptureDeadlineUtc = $CaptureDeadlineUtc; Process = $null
            }
        }
    }
    return $entries
}

function Complete-WpfStabilityDiagnosticCapture {
    param(
        [Parameter(Mandatory = $true)][psobject[]]$Entries,
        [Nullable[DateTime]]$CaptureDeadlineUtc
    )

    foreach ($entry in $Entries) {
        if ($entry.Status -ne 'Started') {
            continue
        }
        $deadlineUtc = [DateTime]::UtcNow.AddSeconds(5)
        if ($null -ne $CaptureDeadlineUtc -and $CaptureDeadlineUtc -lt $deadlineUtc) {
            $deadlineUtc = $CaptureDeadlineUtc
        }
        while (-not $entry.Process.HasExited -and [DateTime]::UtcNow -lt $deadlineUtc) {
            Start-Sleep -Milliseconds 100
        }
        if (-not $entry.Process.HasExited) {
            try {
                Stop-SmartGuardCapturedProcessTree -ProcessId $entry.Process.Id
            }
            catch {
                $entry.Status = 'Failed'
                $entry.Reason = "Unable to terminate diagnostic tool within bounded cleanup: $($_.Exception.Message)"
                continue
            }
            $remainingMilliseconds = if ($null -eq $CaptureDeadlineUtc) {
                5000
            }
            else {
                [Math]::Max(0, [int]($CaptureDeadlineUtc.Subtract([DateTime]::UtcNow).TotalMilliseconds))
            }
            if ($remainingMilliseconds -gt 0 -and -not $entry.Process.WaitForExit($remainingMilliseconds)) {
                $entry.Status = 'Failed'
                $entry.Reason = 'Diagnostic tool did not exit within its bounded 5-second cleanup grace.'
                continue
            }
            if (-not $entry.Process.HasExited) {
                $entry.Status = 'Failed'
                $entry.Reason = 'Diagnostic tool remained alive at the unified timeout-capture deadline.'
                continue
            }
            $entry.Status = 'Failed'
            $entry.Reason = 'Diagnostic tool exceeded its bounded 5-second capture window.'
        }
        else {
            $entry.Process.WaitForExit()
            [System.IO.File]::WriteAllText(
                $entry.StdoutPath,
                $entry.StdoutTask.GetAwaiter().GetResult(),
                [System.Text.UTF8Encoding]::new($false))
            [System.IO.File]::WriteAllText(
                $entry.StderrPath,
                $entry.StderrTask.GetAwaiter().GetResult(),
                [System.Text.UTF8Encoding]::new($false))
            $entry.ExitCode = $entry.Process.ExitCode
            $artifactExists = (Test-Path -LiteralPath $entry.ArtifactPath) -and ((Get-Item -LiteralPath $entry.ArtifactPath).Length -gt 0)
            if ($entry.ExitCode -eq 0 -and $artifactExists) {
                $entry.Status = 'Passed'
            }
            else {
                $entry.Status = 'Failed'
                $entry.Reason = "Diagnostic tool exit code $($entry.ExitCode); expected non-empty artifact: $($entry.ArtifactPath)"
            }
        }
    }
    return $Entries
}

function Start-WpfStabilityController {
    param(
        [Parameter(Mandatory = $true)][string]$ProbeDll,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$ResultDirectory,
        [Parameter(Mandatory = $true)][string]$RunId
    )

    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    if ($null -eq $dotnetCommand -or [string]::IsNullOrWhiteSpace($dotnetCommand.Source)) {
        throw 'dotnet executable could not be resolved from PATH.'
    }
    if (-not (Test-Path -LiteralPath $ProbeDll)) {
        throw "Probe controller output is missing: $ProbeDll"
    }

    [void](New-Item -ItemType Directory -Path $ResultDirectory -Force)
    $stdoutPath = Join-Path $ResultDirectory "dotnet-controller-$RunId.stdout.log"
    $stderrPath = Join-Path $ResultDirectory "dotnet-controller-$RunId.stderr.log"
    $rawArguments = @($ProbeDll) + @($Arguments)
    $launchArguments = @($rawArguments | ForEach-Object {
        ConvertTo-WpfStabilityProbeCmdArgument -Value ([string]$_)
    })
    $process = Start-Process `
        -FilePath $dotnetCommand.Source `
        -ArgumentList $launchArguments `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru
    $process.EnableRaisingEvents = $true
    return [pscustomobject]@{
        Process = $process
        StdoutPath = $stdoutPath
        StderrPath = $stderrPath
        RunId = $RunId
    }
}

function Invoke-WpfStabilityTestSelection {
    param(
        [Parameter(Mandatory = $true)][string]$SettingsTests,
        [Parameter(Mandatory = $true)][string]$Filter,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$ResultDirectory,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    if ($null -eq $dotnetCommand -or [string]::IsNullOrWhiteSpace($dotnetCommand.Source)) {
        throw 'dotnet executable could not be resolved from PATH for WPF test discovery.'
    }

    [void](New-Item -ItemType Directory -Path $ResultDirectory -Force)
    $stdoutPath = Join-Path $ResultDirectory 'test-selection.stdout.log'
    $stderrPath = Join-Path $ResultDirectory 'test-selection.stderr.log'
    $arguments = @(
        'test', $SettingsTests, '--no-build', '--nologo', '--list-tests', '--filter', $Filter
    )
    $launchArguments = @($arguments | ForEach-Object {
        ConvertTo-WpfStabilityProbeCmdArgument -Value ([string]$_)
    })
    $process = Start-Process `
        -FilePath $dotnetCommand.Source `
        -ArgumentList $launchArguments `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru
    $process.EnableRaisingEvents = $true
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds([Math]::Max(30, $TimeoutSeconds))
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadlineUtc) {
        Start-Sleep -Milliseconds 100
    }
    if (-not $process.HasExited) {
        try {
            Stop-SmartGuardCapturedProcessTree -ProcessId $process.Id
        }
        catch {
            throw "WPF test discovery timed out and cleanup failed: $($_.Exception.Message)"
        }
        $cleanupDeadlineUtc = [DateTime]::UtcNow.AddSeconds(5)
        while (-not $process.HasExited -and [DateTime]::UtcNow -lt $cleanupDeadlineUtc) {
            Start-Sleep -Milliseconds 100
        }
        if (-not $process.HasExited -or -not $process.WaitForExit(1000)) {
            throw "WPF test discovery timed out and its process tree did not exit within the bounded 5-second cleanup grace."
        }
        throw "WPF test discovery timed out after $([Math]::Max(30, $TimeoutSeconds)) seconds."
    }
    if (-not $process.WaitForExit(5000)) {
        throw 'WPF test discovery did not exit within its bounded cleanup grace.'
    }

    $stdout = Read-SmartGuardTextFileWhenUnlocked -Path $stdoutPath -TimeoutMilliseconds 5000
    $stderr = if (Test-Path -LiteralPath $stderrPath) {
        Read-SmartGuardTextFileWhenUnlocked -Path $stderrPath -TimeoutMilliseconds 5000
    }
    else {
        ''
    }
    if ($process.ExitCode -ne 0) {
        throw "WPF test discovery failed with exit code $($process.ExitCode): $stderr"
    }

    $names = @(
        $stdout -split "`r?`n" |
            Where-Object { $_ -match '^\s{2,}[A-Za-z0-9_].*$' } |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    if ($names.Count -eq 0) {
        throw "WPF test discovery returned no tests for filter '$Filter'. See $stdoutPath and $stderrPath."
    }

    return [pscustomobject]@{
        Names = $names
        StdoutPath = $stdoutPath
        StderrPath = $stderrPath
        ExitCode = $process.ExitCode
    }
}

function Complete-WpfStabilityController {
    param(
        [Parameter(Mandatory = $true)][psobject]$Entry,
        [Parameter(Mandatory = $true)][DateTime]$DeadlineUtc,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    while (-not $Entry.Process.HasExited -and [DateTime]::UtcNow -lt $DeadlineUtc) {
        Start-Sleep -Milliseconds 100
    }
    $timedOut = -not $Entry.Process.HasExited
    $cleanupTimedOut = $false
    $cleanupError = ''
    if ($timedOut) {
        try {
            Stop-SmartGuardCapturedProcessTree -ProcessId $Entry.Process.Id
        }
        catch {
            $cleanupError = $_.Exception.Message
        }
    }
    $processExited = $Entry.Process.WaitForExit(5000)
    if (-not $processExited) {
        $cleanupTimedOut = $true
        if ([string]::IsNullOrWhiteSpace($cleanupError)) {
            $cleanupError = 'The controller did not exit within its bounded 5-second cleanup grace.'
        }
    }
    $stdout = if (Test-Path -LiteralPath $Entry.StdoutPath) {
        try { Read-SmartGuardTextFileWhenUnlocked -Path $Entry.StdoutPath -TimeoutMilliseconds 5000 } catch { '' }
    }
    else {
        ''
    }
    $stderr = if (Test-Path -LiteralPath $Entry.StderrPath) {
        try { Read-SmartGuardTextFileWhenUnlocked -Path $Entry.StderrPath -TimeoutMilliseconds 5000 } catch { '' }
    }
    else {
        ''
    }
    $output = (@($stdout, $stderr) | Where-Object { $_ -ne '' }) -join [Environment]::NewLine
    if ($timedOut) {
        $output = (@($output, "Command timed out after $TimeoutSeconds seconds (PID $($Entry.Process.Id)).")) -join [Environment]::NewLine
    }
    return [pscustomobject]@{
        ExitCode = if ($processExited) { $Entry.Process.ExitCode } else { $null }
        Output = $output.TrimEnd()
        TimedOut = $timedOut
        CleanupTimedOut = $cleanupTimedOut
        CleanupError = $cleanupError
        RunId = $Entry.RunId
    }
}

function Set-WpfStabilityProbeControllerTimeoutEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][bool]$ControllerTimedOut
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "WPF stability probe result was not found while recording controller timeout evidence: $Path"
    }
    $json = [System.IO.File]::ReadAllText(
        $Path,
        [System.Text.UTF8Encoding]::new($false, $true))
    $result = $json | ConvertFrom-Json -ErrorAction Stop
    $existing = if ($result.PSObject.Properties.Name -contains 'ControllerTimedOut') {
        [bool]$result.ControllerTimedOut
    }
    else {
        $false
    }
    $effective = $existing -or $ControllerTimedOut
    $result | Add-Member -NotePropertyName ControllerTimedOut -NotePropertyValue $effective -Force
    [System.IO.File]::WriteAllText(
        $Path,
        ($result | ConvertTo-Json -Depth 10),
        [System.Text.UTF8Encoding]::new($false))
}

function Write-WpfStabilityCaptureStatus {
    param(
        [Parameter(Mandatory = $true)][string]$RunDirectory,
        [Parameter(Mandatory = $true)][int]$ControllerRootPid,
        [Nullable[int]]$TestHostPid,
        [Parameter(Mandatory = $true)][psobject[]]$ToolEntries,
        [Nullable[DateTime]]$FailureCaptureUtc,
        [string]$FailureCaptureReason = '',
        [Parameter(Mandatory = $true)][bool]$ControllerTimedOut
    )

    $capture = [ordered]@{
        ControllerRootPid = $ControllerRootPid
        ControllerTimedOut = $ControllerTimedOut
        TestHostPid = $TestHostPid
        CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
        FailureCaptureUtc = if ($null -eq $FailureCaptureUtc) { $null } else { $FailureCaptureUtc.ToString('O') }
        FailureCaptureReason = $FailureCaptureReason
        Tools = @($ToolEntries | ForEach-Object {
            [ordered]@{
                Tool = $_.Tool; ToolPath = $_.ToolPath; Status = $_.Status; Reason = $_.Reason;
                Command = $_.Command; ExitCode = $_.ExitCode; StdoutPath = $_.StdoutPath;
                StderrPath = $_.StderrPath; ArtifactPath = $_.ArtifactPath; CapturePhase = $_.CapturePhase;
                CaptureDeadlineUtc = if ($null -eq $_.CaptureDeadlineUtc) { $null } else { $_.CaptureDeadlineUtc.ToString('O') }
            }
        })
    }
    $path = Join-Path $RunDirectory 'capture-status.json'
    [System.IO.File]::WriteAllText(
        $path,
        ($capture | ConvertTo-Json -Depth 5),
        [System.Text.UTF8Encoding]::new($false))
    return $path
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$probeProject = Join-Path $repoRoot 'Tests\SmartGuard.Settings.StabilityProbe\SmartGuard.Settings.StabilityProbe.csproj'
$settingsTests = Join-Path $repoRoot 'Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj'
$resultRoot = Join-Path $repoRoot '.work\test-results\wpf-stability'
$sharedRunner = Join-Path $repoRoot 'scripts\Invoke-SmartGuardDotNetTests.ps1'
$probeDll = Join-Path $repoRoot 'Tests\SmartGuard.Settings.StabilityProbe\bin\Debug\net8.0\SmartGuard.Settings.StabilityProbe.dll'
$ToolRoot = [System.IO.Path]::GetFullPath($ToolRoot)

if (-not (Test-Path -LiteralPath $probeProject)) {
    throw "WPF stability probe project is missing: $probeProject"
}
if (-not (Test-Path -LiteralPath $settingsTests)) {
    throw "WPF Settings test project is missing: $settingsTests"
}
. $sharedRunner
[void](New-Item -ItemType Directory -Path $resultRoot -Force)

$restoreOutput = dotnet restore $probeProject --nologo 2>&1
$restoreOutput | Write-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed for $probeProject"
}
$buildOutput = dotnet build $probeProject --no-restore --nologo -v q 2>&1
$buildOutput | Write-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed for $probeProject"
}
if (-not (Test-Path -LiteralPath $probeDll)) {
    throw "Probe controller output is missing after build: $probeDll"
}

$settingsBuildOutput = dotnet build $settingsTests --no-restore --nologo -v q 2>&1
$settingsBuildOutput | Write-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed for $settingsTests"
}

$selectionDirectory = Join-Path $resultRoot ('.selection-' + [guid]::NewGuid().ToString('N'))
$selection = Invoke-WpfStabilityTestSelection `
    -SettingsTests $settingsTests `
    -Filter $Filter `
    -WorkingDirectory $repoRoot `
    -ResultDirectory $selectionDirectory `
    -TimeoutSeconds $TimeoutSeconds
$selectionTests = @($selection.Names)

$results = @()
for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    do {
        $runId = '{0}-i{1}-s{2}-{3}' -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'), $iteration, $Seed, [guid]::NewGuid().ToString('N')
        $runDirectory = Join-Path $resultRoot $runId
    } while (Test-Path -LiteralPath $runDirectory)

    $trxPath = Join-Path $runDirectory 'test-results.trx'
    $testCommand = $null
    if ($Mode -eq 'test-contamination') {
        $testCommand = @(
            'dotnet test',
            (ConvertTo-WpfStabilityProbeCmdArgument $settingsTests),
            '--no-build --nologo -v q',
            '--filter',
            (ConvertTo-WpfStabilityProbeCmdArgument $Filter),
            '--logger',
            (ConvertTo-WpfStabilityProbeCmdArgument 'trx;LogFileName=test-results.trx'),
            '--results-directory',
            (ConvertTo-WpfStabilityProbeCmdArgument $runDirectory)
        ) -join ' '
    }

    $controllerCaptureDirectory = Join-Path $resultRoot ('.controller-' + $runId)
    [void](New-Item -ItemType Directory -Path $controllerCaptureDirectory -Force)
    $selectionPath = Join-Path $controllerCaptureDirectory 'test-selection.txt'
    [System.IO.File]::WriteAllLines(
        $selectionPath,
        $selectionTests,
        [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Copy(
        $selection.StdoutPath,
        (Join-Path $controllerCaptureDirectory 'test-selection.stdout.log'),
        $true)
    [System.IO.File]::Copy(
        $selection.StderrPath,
        (Join-Path $controllerCaptureDirectory 'test-selection.stderr.log'),
        $true)
    $controllerArguments = @(
        '--mode', $Mode,
        '--run-directory', $runDirectory,
        '--iteration', $iteration,
        '--seed', ($Seed + $iteration - 1),
        '--filter', $Filter,
        '--timeout-seconds', $TimeoutSeconds,
        '--working-directory', $repoRoot,
        '--trx-path', $trxPath,
        '--selection-path', $selectionPath
    )
    if ($null -ne $testCommand) {
        $testCommandPath = Join-Path $controllerCaptureDirectory 'test-command.txt'
        [System.IO.File]::WriteAllText(
            $testCommandPath,
            $testCommand,
            [System.Text.UTF8Encoding]::new($false))
        $controllerArguments += @('--test-command-file', $testCommandPath)
    }

    $controllerStartedUtc = [DateTime]::UtcNow
    $controllerCapture = Start-WpfStabilityController `
        -ProbeDll $probeDll `
        -Arguments $controllerArguments `
        -WorkingDirectory $repoRoot `
        -ResultDirectory $controllerCaptureDirectory `
        -RunId ('controller-' + $runId)
    $controllerDeadlineUtc = $controllerStartedUtc.AddSeconds($TimeoutSeconds)
    $testHostPid = $null
    $diagnosticEntries = @()
    $failureCaptureUtc = $null
    $failureCaptureReason = ''
    while (-not $controllerCapture.Process.HasExited -and [DateTime]::UtcNow -lt $controllerDeadlineUtc) {
        if ($null -eq $testHostPid) {
            $testHostPid = Resolve-WpfStabilityTestHostPid `
                -RootProcessId $controllerCapture.Process.Id `
                -StartedUtc $controllerStartedUtc
            if ($null -ne $testHostPid) {
                $diagnosticEntries = @(Start-WpfStabilityDiagnosticCapture `
                    -ToolRoot $ToolRoot `
                    -RunDirectory $runDirectory `
                    -TestHostPid $testHostPid `
                    -CapturePhase live)
            }
        }
        Start-Sleep -Milliseconds 100
    }

    $controllerTimedOut = -not $controllerCapture.Process.HasExited
    $timeoutCaptureDeadlineUtc = if ($controllerTimedOut) {
        $controllerDeadlineUtc.AddSeconds(5)
    }
    else {
        $null
    }
    if ($controllerTimedOut) {
        $failureCaptureUtc = [DateTime]::UtcNow
        if ($null -eq $testHostPid) {
            $failureCaptureReason = 'The controller reached its deadline but the actual testhost PID was not resolved.'
        }
        else {
            $timeoutEntries = @(Start-WpfStabilityDiagnosticCapture `
                -ToolRoot $ToolRoot `
                -RunDirectory $runDirectory `
                -TestHostPid $testHostPid `
                -ArtifactPrefix 'timeout-' `
                -CapturePhase timeout-pre-terminate `
                -CaptureDeadlineUtc $timeoutCaptureDeadlineUtc)
            $diagnosticEntries += $timeoutEntries
            # Capture while the target is still alive, before timeout cleanup
            # terminates the exact controller tree.
            $diagnosticEntries = @(Complete-WpfStabilityDiagnosticCapture `
                -Entries $diagnosticEntries `
                -CaptureDeadlineUtc $timeoutCaptureDeadlineUtc)
        }
    }

    $controllerCompletionDeadlineUtc = if ($controllerTimedOut) {
        # The controller owns a bounded five-second handoff after its test
        # timeout. Diagnostics stop at that deadline; the outer controller
        # gets one additional bounded finalization margin to persist its
        # timeout/TRX/cleanup receipt before the exact tree is terminated.
        $timeoutCaptureDeadlineUtc.AddSeconds(2)
    }
    else {
        $controllerDeadlineUtc
    }
    $controllerResult = Complete-WpfStabilityController `
        -Entry $controllerCapture `
        -DeadlineUtc $controllerCompletionDeadlineUtc `
        -TimeoutSeconds $TimeoutSeconds
    $controllerEvidenceError = ''
    try {
        Set-WpfStabilityProbeControllerTimeoutEvidence `
            -Path (Join-Path $runDirectory 'probe-result.json') `
            -ControllerTimedOut:$controllerTimedOut
    }
    catch {
        $controllerEvidenceError = $_.Exception.Message
    }
    if ($diagnosticEntries.Count -eq 0) {
        $diagnosticEntries = @(Start-WpfStabilityDiagnosticCapture `
            -ToolRoot $ToolRoot `
            -RunDirectory $runDirectory `
            -TestHostPid $null)
    }
    $diagnosticEntries = @(Complete-WpfStabilityDiagnosticCapture -Entries $diagnosticEntries)
    $probeResultPath = Join-Path $runDirectory 'probe-result.json'
    $probeResult = Read-SmartGuardWpfStabilityProbeResult -Path $probeResultPath
    $capturePath = Write-WpfStabilityCaptureStatus `
        -RunDirectory $runDirectory `
        -ControllerRootPid $controllerCapture.Process.Id `
        -TestHostPid $testHostPid `
        -ToolEntries $diagnosticEntries `
        -FailureCaptureUtc $failureCaptureUtc `
        -FailureCaptureReason $failureCaptureReason `
        -ControllerTimedOut:$controllerTimedOut
    $captureStatus = if (@($diagnosticEntries | Where-Object { $_.Status -ne 'Passed' }).Count -eq 0) {
        'Passed'
    }
    else {
        'Failed'
    }
    $results += [pscustomobject]@{
        RunId = $runId
        RunDirectory = $runDirectory
        ControllerExitCode = $controllerResult.ExitCode
        ControllerTimedOut = $controllerTimedOut
        ControllerEvidenceError = $controllerEvidenceError
        Result = $probeResult
        CaptureStatusPath = $capturePath
        CaptureStatus = $captureStatus
    }
}

$results
exit (Get-SmartGuardWpfStabilityProbeExitCode -Results $results -RequireReproduction:$RequireReproduction)
