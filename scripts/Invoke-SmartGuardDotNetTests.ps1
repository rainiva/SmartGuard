function Get-SmartGuardTestTier {
    $tier = $env:SMARTGUARD_TEST_TIER
    if ([string]::IsNullOrWhiteSpace($tier)) {
        return 'full'
    }

    switch ($tier.ToLowerInvariant()) {
        'fast' { return 'fast' }
        'nightly' { return 'nightly' }
        default { return 'full' }
    }
}

function Get-SmartGuardDotNetTestFilter {
    param([string]$Tier = (Get-SmartGuardTestTier))

    switch ($Tier) {
        'fast' { return 'Category!=Performance&Category!=Live&Category!=WpfUi' }
        'full' { return 'Category!=Performance&Category!=Live' }
        'nightly' { return $null }
        default { return 'Category!=Performance&Category!=Live' }
    }
}

function Get-SmartGuardDotNetTestProjectPaths {
    param(
        [string]$RepoRoot,
        [string]$Tier = (Get-SmartGuardTestTier)
    )

    $projects = @(
        (Join-Path $RepoRoot 'Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Cli.Tests\SmartGuard.Cli.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Tray.Tests\SmartGuard.Tray.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Settings.LogView.Tests\SmartGuard.Settings.LogView.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Packaging.Tests\SmartGuard.Packaging.Tests.csproj')
    )

    if ($Tier -eq 'nightly') {
        $projects += Join-Path $RepoRoot 'Tests\SmartGuard.Engine.PerformanceTests\SmartGuard.Engine.PerformanceTests.csproj'
    }

    return $projects
}

function Get-SmartGuardParallelDotNetTestProjectPaths {
    param([string]$RepoRoot)

    @(
        (Join-Path $RepoRoot 'Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Cli.Tests\SmartGuard.Cli.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Packaging.Tests\SmartGuard.Packaging.Tests.csproj')
    )
}

function Get-SmartGuardSerialDotNetTestProjectPaths {
    param(
        [string]$RepoRoot,
        [string]$Tier = (Get-SmartGuardTestTier)
    )

    $projects = @(
        (Join-Path $RepoRoot 'Tests\SmartGuard.Tray.Tests\SmartGuard.Tray.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Settings.LogView.Tests\SmartGuard.Settings.LogView.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj')
    )

    if ($Tier -eq 'nightly') {
        $projects += Join-Path $RepoRoot 'Tests\SmartGuard.Engine.PerformanceTests\SmartGuard.Engine.PerformanceTests.csproj'
    }

    return $projects
}

function Ensure-SmartGuardEnginePublished {
    param([string]$RepoRoot)

    $engineExe = Join-Path $RepoRoot 'bin\SmartGuard.Engine.exe'
    if (Test-Path -LiteralPath $engineExe) {
        return
    }

    $buildCmd = Join-Path $RepoRoot 'build.cmd'
    if (-not (Test-Path -LiteralPath $buildCmd)) {
        throw "Missing publish output: $engineExe (run build.cmd)"
    }

    Write-Host "Publishing desktop suite for integration tests..."
    & $buildCmd
    if ($LASTEXITCODE -ne 0) {
        throw "build.cmd failed with exit code $LASTEXITCODE"
    }
}

function Ensure-SmartGuardDotNetTestBuild {
    param(
        [string]$RepoRoot,
        [string[]]$ProjectPaths
    )

    foreach ($project in $ProjectPaths) {
        if (-not (Test-Path -LiteralPath $project)) {
            throw "Missing test project: $project"
        }

        $output = dotnet build $project --no-restore --nologo -v q 2>&1
        $output | Write-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for $project"
        }
    }
}

function Read-SmartGuardTextFileWhenUnlocked {
    param(
        [string]$Path,
        [int]$TimeoutMilliseconds = 30000
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while ($true) {
        try {
            return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
        }
        catch [System.IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw
            }
            Start-Sleep -Milliseconds 50
        }
    }
}

if ($null -eq ('SmartGuardCapturedProcessTreeSnapshot' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class SmartGuardCapturedProcessTreeSnapshot
{
    private const uint SnapshotProcesses = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint ThreadCount;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static int[] GetProcessIds(int rootProcessId)
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandleValue)
        {
            throw new InvalidOperationException(
                "CreateToolhelp32Snapshot failed with Win32 error " +
                Marshal.GetLastWin32Error());
        }

        var childrenByParent = new Dictionary<int, List<int>>();
        try
        {
            var entry = new ProcessEntry();
            entry.Size = (uint)Marshal.SizeOf(typeof(ProcessEntry));
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    var parentProcessId = (int)entry.ParentProcessId;
                    var processId = (int)entry.ProcessId;
                    List<int> children;
                    if (!childrenByParent.TryGetValue(parentProcessId, out children))
                    {
                        children = new List<int>();
                        childrenByParent[parentProcessId] = children;
                    }
                    children.Add(processId);
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var result = new List<int>();
        var pending = new Queue<int>();
        var visited = new HashSet<int>();
        pending.Enqueue(rootProcessId);
        while (pending.Count > 0)
        {
            var processId = pending.Dequeue();
            if (!visited.Add(processId))
            {
                continue;
            }

            result.Add(processId);
            List<int> children;
            if (!childrenByParent.TryGetValue(processId, out children))
            {
                continue;
            }
            foreach (var childProcessId in children)
            {
                pending.Enqueue(childProcessId);
            }
        }

        return result.ToArray();
    }
}
'@
}

function Stop-SmartGuardCapturedProcessTree {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ProcessId
    )

    $taskkillPath = Join-Path $env:SystemRoot 'System32\taskkill.exe'
    if (-not (Test-Path -LiteralPath $taskkillPath)) {
        throw "Unable to stop captured process tree because taskkill.exe is missing: $taskkillPath"
    }

    $capturedTreeProcessIds = @(
        [SmartGuardCapturedProcessTreeSnapshot]::GetProcessIds($ProcessId)
    )

    & $taskkillPath /PID $ProcessId /T /F 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    [array]::Reverse($capturedTreeProcessIds)
    foreach ($capturedProcessId in $capturedTreeProcessIds) {
        Stop-Process -Id $capturedProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Start-SmartGuardCapturedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandText,

        [Parameter(Mandatory = $true)]
        [string]$ResultDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RunId
    )

    if ([string]::IsNullOrWhiteSpace($CommandText)) {
        throw 'CommandText must not be empty.'
    }
    if ([string]::IsNullOrWhiteSpace($RunId)) {
        throw 'RunId must not be empty.'
    }

    [void](New-Item -ItemType Directory -Path $ResultDirectory -Force)
    $stdoutPath = Join-Path $ResultDirectory "dotnet-test-$RunId.stdout.log"
    $stderrPath = Join-Path $ResultDirectory "dotnet-test-$RunId.stderr.log"
    $commandArgument = '"{0}"' -f $CommandText
    $process = Start-Process `
        -FilePath $env:ComSpec `
        -ArgumentList @('/d', '/c', $commandArgument) `
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

function Complete-SmartGuardCapturedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Entry,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [int]::MaxValue)]
        [int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $Entry.Process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }

    $timedOut = -not $Entry.Process.HasExited
    if ($timedOut) {
        Stop-SmartGuardCapturedProcessTree -ProcessId $Entry.Process.Id
    }

    $Entry.Process.WaitForExit()
    $exitCode = $Entry.Process.ExitCode
    $outputParts = @()
    if (Test-Path -LiteralPath $Entry.StdoutPath) {
        $outputParts += Read-SmartGuardTextFileWhenUnlocked -Path $Entry.StdoutPath
    }
    if ($Entry.PSObject.Properties.Name -contains 'StderrPath' -and
        (Test-Path -LiteralPath $Entry.StderrPath)) {
        $outputParts += Read-SmartGuardTextFileWhenUnlocked -Path $Entry.StderrPath
    }

    $output = ($outputParts -join [Environment]::NewLine).TrimEnd()
    if ($timedOut) {
        $timeoutMessage = "Command timed out after $TimeoutSeconds seconds (PID $($Entry.Process.Id))."
        $output = (@($output, $timeoutMessage) | Where-Object { $_ -ne '' }) -join [Environment]::NewLine
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
        TimedOut = $timedOut
        RunId = $Entry.RunId
    }
}

function Read-SmartGuardTrxResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [int]$ExitCode
    )

    $failedResult = [ordered]@{
        Result = 'Failed'
        PassedCount = 0
        FailedCount = 0
        SkippedCount = 0
        TotalCount = 0
        ExecutedCount = 0
        ErrorCount = 0
        TimeoutCount = 0
        AbortedCount = 0
        InconclusiveCount = 0
        NotExecutedCount = 0
        TrxPath = $Path
        ExitCode = $ExitCode
        Error = ''
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        $failedResult.Error = "TRX file was not found: $Path"
        if ($ExitCode -ne 0) {
            $failedResult.Error += " (dotnet test exit code $ExitCode)"
        }
        return [pscustomobject]$failedResult
    }

    try {
        $trxText = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false, $true))
        $trx = [xml]$trxText
        $summaryNode = $trx.SelectSingleNode(
            "/*[local-name()='TestRun']/*[local-name()='ResultSummary']")
        if ($null -eq $summaryNode) {
            throw 'TRX ResultSummary is missing.'
        }

        $countersNode = $summaryNode.SelectSingleNode("*[local-name()='Counters']")
        if ($null -eq $countersNode) {
            throw 'TRX ResultSummary/Counters is missing.'
        }

        $counterValues = @{}
        foreach ($counterName in @(
            'total',
            'executed',
            'passed',
            'failed',
            'error',
            'timeout',
            'aborted',
            'inconclusive',
            'notExecuted'
        )) {
            $attribute = $countersNode.Attributes.GetNamedItem($counterName)
            $value = 0
            if ($null -eq $attribute -or
                -not [int]::TryParse($attribute.Value, [ref]$value) -or
                $value -lt 0) {
                throw "TRX counter '$counterName' is missing or invalid."
            }
            $counterValues[$counterName] = $value
        }

        $outcome = $summaryNode.Attributes.GetNamedItem('outcome')
        $outcomeValue = if ($null -eq $outcome) { '' } else { $outcome.Value }
        $failedResult.PassedCount = $counterValues.passed
        $failedResult.FailedCount =
            $counterValues.failed +
            $counterValues.error +
            $counterValues.timeout +
            $counterValues.aborted
        $failedResult.SkippedCount =
            $counterValues.inconclusive +
            $counterValues.notExecuted
        $failedResult.TotalCount = $counterValues.total
        $failedResult.ExecutedCount = $counterValues.executed
        $failedResult.ErrorCount = $counterValues.error
        $failedResult.TimeoutCount = $counterValues.timeout
        $failedResult.AbortedCount = $counterValues.aborted
        $failedResult.InconclusiveCount = $counterValues.inconclusive
        $failedResult.NotExecutedCount = $counterValues.notExecuted

        $errors = @()
        if ($ExitCode -ne 0) {
            $errors += "dotnet test exited with code $ExitCode"
        }
        if ($outcomeValue -ne 'Completed') {
            $errors += "TRX outcome is '$outcomeValue', expected 'Completed'"
        }
        if ($counterValues.total -le 0) {
            $errors += 'TRX total must be greater than zero'
        }

        $failureClassCount =
            $counterValues.failed +
            $counterValues.error +
            $counterValues.timeout +
            $counterValues.aborted +
            $counterValues.inconclusive +
            $counterValues.notExecuted
        if ($failureClassCount -ne 0) {
            $errors += "TRX contains $failureClassCount failed, aborted, inconclusive, or not-executed tests"
        }

        if ($errors.Count -eq 0) {
            $failedResult.Result = 'Passed'
        }
        else {
            $failedResult.Error = $errors -join '; '
        }

        return [pscustomobject]$failedResult
    }
    catch {
        $failedResult.Error = "Unable to parse TRX '$Path': $($_.Exception.Message)"
        return [pscustomobject]$failedResult
    }
}

function ConvertTo-SmartGuardCmdArgument {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    return '"{0}"' -f $Value.Replace('"', '""')
}

function Invoke-SmartGuardDotNetTestProcesses {
    param(
        [string[]]$ProjectPaths,
        [string]$Filter,
        [switch]$NoBuild,
        [string]$ResultDirectory = (Join-Path $PSScriptRoot '..\.work\test-results'),
        [string]$RunId = ([guid]::NewGuid().ToString('N')),
        [int]$TimeoutSeconds = 600
    )

    [void](New-Item -ItemType Directory -Path $resultDirectory -Force)
    $entries = @()
    foreach ($project in $ProjectPaths) {
        $arguments = @('test', $project, '--nologo', '-v', 'q')
        if ($Filter) {
            $arguments += @('--filter', $Filter)
        }
        if ($NoBuild) {
            $arguments += '--no-build'
        }

        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
        $projectRunId = "$RunId-$projectName"
        $trxFileName = "$projectRunId.trx"
        $trxPath = Join-Path $resultDirectory $trxFileName
        $arguments += @(
            '--logger',
            "trx;LogFileName=$trxFileName",
            '--results-directory',
            $resultDirectory
        )
        $argumentText = ($arguments | ForEach-Object {
            ConvertTo-SmartGuardCmdArgument -Value $_
        }) -join ' '
        $entry = Start-SmartGuardCapturedCommand `
            -CommandText "dotnet $argumentText" `
            -ResultDirectory $resultDirectory `
            -RunId $projectRunId
        $entries += [pscustomobject]@{
            Process = $entry.Process
            Project = $project
            StdoutPath = $entry.StdoutPath
            StderrPath = $entry.StderrPath
            RunId = $entry.RunId
            TrxPath = $trxPath
        }
    }

    $results = @()
    foreach ($entry in $entries) {
        $captureResult = Complete-SmartGuardCapturedCommand `
            -Entry $entry `
            -TimeoutSeconds $TimeoutSeconds
        $trxResult = Read-SmartGuardTrxResult `
            -Path $entry.TrxPath `
            -ExitCode $captureResult.ExitCode
        if ($captureResult.TimedOut) {
            $trxResult.Result = 'Failed'
            $timeoutError = "dotnet test timed out after $TimeoutSeconds seconds"
            $trxResult.Error = (@($timeoutError, $trxResult.Error) | Where-Object { $_ -ne '' }) -join '; '
        }

        $results += [pscustomobject]@{
            Project = $entry.Project
            RunId = $entry.RunId
            TrxPath = $entry.TrxPath
            ExitCode = $captureResult.ExitCode
            Output = $captureResult.Output
            TimedOut = $captureResult.TimedOut
            Result = $trxResult.Result
            PassedCount = $trxResult.PassedCount
            FailedCount = $trxResult.FailedCount
            SkippedCount = $trxResult.SkippedCount
            TotalCount = $trxResult.TotalCount
            Error = $trxResult.Error
        }
    }

    return $results
}

function Write-SmartGuardDotNetTestResults {
    param(
        [array]$Results
    )

    foreach ($result in $Results) {
        if ($result.Output) {
            Write-Host $result.Output.TrimEnd()
        }
        if ($result.Result -ne 'Passed') {
            $name = Split-Path -Leaf $result.Project
            Write-Host "dotnet test ($name) FAILED: $($result.Error)" -ForegroundColor Red
        }
    }
}

function New-SmartGuardDotNetTestSuiteResult {
    param(
        [array]$ProjectResults
    )

    $projectResultsArray = @($ProjectResults)
    $failedProjects = @($projectResultsArray | Where-Object { $_.Result -ne 'Passed' })
    $suiteResult = if ($projectResultsArray.Count -gt 0 -and $failedProjects.Count -eq 0) {
        'Passed'
    }
    else {
        'Failed'
    }

    return [pscustomobject]@{
        Result = $suiteResult
        PassedCount = [int](($projectResultsArray | Measure-Object -Property PassedCount -Sum).Sum)
        FailedCount = [int](($projectResultsArray | Measure-Object -Property FailedCount -Sum).Sum)
        SkippedCount = [int](($projectResultsArray | Measure-Object -Property SkippedCount -Sum).Sum)
        TotalCount = [int](($projectResultsArray | Measure-Object -Property TotalCount -Sum).Sum)
        ProjectResults = $projectResultsArray
    }
}

function Invoke-SmartGuardDotNetTestSuite {
    param(
        [string]$RepoRoot,
        [string]$Tier = (Get-SmartGuardTestTier)
    )

    $projects = Get-SmartGuardDotNetTestProjectPaths -RepoRoot $RepoRoot -Tier $Tier
    $parallelProjects = Get-SmartGuardParallelDotNetTestProjectPaths -RepoRoot $RepoRoot
    $serialProjects = Get-SmartGuardSerialDotNetTestProjectPaths -RepoRoot $RepoRoot -Tier $Tier
    $filter = Get-SmartGuardDotNetTestFilter -Tier $Tier

    try {
        Ensure-SmartGuardEnginePublished -RepoRoot $RepoRoot
        Ensure-SmartGuardDotNetTestBuild -RepoRoot $RepoRoot -ProjectPaths $projects

        $suiteRunId = [guid]::NewGuid().ToString('N')
        $parallelResults = Invoke-SmartGuardDotNetTestProcesses `
            -ProjectPaths $parallelProjects `
            -Filter $filter `
            -NoBuild `
            -RunId $suiteRunId
        $serialResults = @()
        foreach ($project in $serialProjects) {
            $serialResults += Invoke-SmartGuardDotNetTestProcesses `
                -ProjectPaths @($project) `
                -Filter $filter `
                -NoBuild `
                -RunId $suiteRunId
        }

        $projectResults = @($parallelResults) + @($serialResults)
        Write-SmartGuardDotNetTestResults -Results $projectResults
        return New-SmartGuardDotNetTestSuiteResult -ProjectResults $projectResults
    }
    catch {
        $infrastructureFailure = [pscustomobject]@{
            Project = '<dotnet-test-infrastructure>'
            RunId = ''
            TrxPath = ''
            ExitCode = 1
            Output = ''
            TimedOut = $false
            Result = 'Failed'
            PassedCount = 0
            FailedCount = 0
            SkippedCount = 0
            TotalCount = 0
            Error = $_.Exception.Message
        }
        Write-SmartGuardDotNetTestResults -Results @($infrastructureFailure)
        return New-SmartGuardDotNetTestSuiteResult -ProjectResults @($infrastructureFailure)
    }
}

function Test-SmartGuardTrayIntegrationEnabled {
    param([string]$Tier = (Get-SmartGuardTestTier))

    return $Tier -ne 'fast'
}

function Test-SmartGuardInstallerIntegrationEnabled {
    param([string]$Tier = (Get-SmartGuardTestTier))

    if ($Tier -eq 'fast') {
        return $false
    }

    if ($env:SMARTGUARD_SKIP_INSTALLER_TESTS -eq '1') {
        return $false
    }

    return $true
}
