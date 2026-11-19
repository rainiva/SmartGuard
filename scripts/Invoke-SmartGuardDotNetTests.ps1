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

function Test-SmartGuardEnginePublishStale {
    param(
        [string]$RepoRoot,
        [string]$EngineExePath
    )

    $publishedAtUtc = (Get-Item -LiteralPath $EngineExePath).LastWriteTimeUtc
    $sourceRoot = Join-Path $RepoRoot 'src'
    if (-not (Test-Path -LiteralPath $sourceRoot)) {
        return $false
    }

    $newestSource = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    return ($null -ne $newestSource) -and ($newestSource.LastWriteTimeUtc -gt $publishedAtUtc)
}

function Ensure-SmartGuardEnginePublished {
    param([string]$RepoRoot)

    # Existence alone is not enough: a published engine older than the newest
    # source kept integration tests exercising stale binaries without the
    # latest crash fixes.
    $engineExe = Join-Path $RepoRoot 'bin\SmartGuard.Engine.exe'
    if ((Test-Path -LiteralPath $engineExe) -and
        -not (Test-SmartGuardEnginePublishStale -RepoRoot $RepoRoot -EngineExePath $engineExe)) {
        return
    }

    $buildCmd = Join-Path $RepoRoot 'build.cmd'
    if (-not (Test-Path -LiteralPath $buildCmd)) {
        throw "Missing publish output: $engineExe (run build.cmd)"
    }

    Write-Host "Publishing desktop suite for integration tests..."
    $buildOutput = & $buildCmd 2>&1
    $buildExitCode = $LASTEXITCODE
    $buildOutput | Write-Host
    if ($buildExitCode -ne 0) {
        throw "build.cmd failed with exit code $buildExitCode"
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
            # FileShare ReadWrite: MSBuild nodeReuse workers inherit the
            # redirected stdout handle and keep it open after dotnet test
            # exits, so an exclusive read would fail forever.
            $stream = [System.IO.File]::Open(
                $Path,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::ReadWrite)
            try {
                $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
                return $reader.ReadToEnd()
            }
            finally {
                $stream.Dispose()
            }
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

    # Pester 5 在 It 内把 ErrorActionPreference 设为 Stop；PS 5.1 下原生命令的
    # stderr 经 2>$null 重定向会被包装成 terminating NativeCommandError，
    # 导致 taskkill 失败信息直接炸掉调用方而不是走 Stop-Process 回退。
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $taskkillPath /PID $ProcessId /T /F 2>$null | Out-Null
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
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

    $deadline = if ($Entry.PSObject.Properties.Name -contains 'DeadlineUtc') {
        [DateTime]$Entry.DeadlineUtc
    }
    else {
        [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    }
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
        # xUnit reports a skipped test only as total > executed (Counters.notExecuted stays 0);
        # inconclusive and notExecuted remain failure classes below.
        $failedResult.SkippedCount = [Math]::Max(0, $counterValues.total - $counterValues.executed)
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

function Read-SmartGuardWpfStabilityProbeResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $failed = [ordered]@{
        Status = 'Failed'
        Error = ''
        ResultPath = $Path
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        $failed.Error = "WPF stability probe result was not found: $Path"
        return [pscustomobject]$failed
    }

    try {
        $json = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false, $true))
        $result = $json | ConvertFrom-Json -ErrorAction Stop
        foreach ($field in @(
            'Status', 'ExecutionStatus', 'ReproductionStatus', 'Mode', 'Iteration', 'Seed',
            'Filter', 'ControllerPid', 'StartUtc', 'EndUtc', 'ExitCode', 'TimedOut', 'TrxPath',
            'StdoutPath', 'StderrPath', 'TestOrderPath', 'TrxStatus', 'TotalCount',
            'ControllerTimedOut', 'CleanupTimedOut', 'CleanupError'
        )) {
            if ($result.PSObject.Properties.Name -notcontains $field) {
                throw "Probe result field '$field' is missing."
            }
        }

        if ($result.Status -notin @('Passed', 'Failed', 'Blocked')) {
            throw "Probe result has invalid status '$($result.Status)'."
        }
        if ($result.ExecutionStatus -notin @('Passed', 'Failed', 'Blocked') -or
            $result.ExecutionStatus -ne $result.Status) {
            throw 'Probe result execution status is missing or inconsistent with Status.'
        }
        if ($result.ReproductionStatus -notin @('NotReproduced', 'Reproduced', 'Blocked')) {
            throw "Probe result has invalid reproduction status '$($result.ReproductionStatus)'."
        }
        if ($result.Mode -notin @('test-contamination', 'production-equivalent')) {
            throw "Probe result has invalid mode '$($result.Mode)'."
        }
        if ($result.Mode -eq 'production-equivalent' -and $result.ReproductionStatus -ne 'Blocked') {
            throw 'production-equivalent probe results must remain reproduction-blocked until a no-hook selector exists.'
        }
        if ([int]$result.Iteration -lt 1 -or [int]$result.ControllerPid -lt 1) {
            throw 'Probe result iteration or controller PID is invalid.'
        }
        if ([string]::IsNullOrWhiteSpace([string]$result.Filter)) {
            throw 'Probe result filter is missing or empty.'
        }
        if ([bool]$result.ControllerTimedOut -and $result.Status -eq 'Passed') {
            throw 'A probe result cannot be Passed after the outer controller reached its deadline.'
        }
        if ([int]$result.TotalCount -le 0 -and $result.Status -eq 'Passed') {
            throw 'A passed probe result must report at least one executed test.'
        }

        if ([string]::IsNullOrWhiteSpace([string]$result.TestOrderPath) -or
            -not (Test-Path -LiteralPath ([string]$result.TestOrderPath))) {
            throw "Probe test-order manifest was not found: $($result.TestOrderPath)"
        }
        $orderText = [System.IO.File]::ReadAllText(
            [string]$result.TestOrderPath,
            [System.Text.UTF8Encoding]::new($false, $true))
        $order = $orderText | ConvertFrom-Json -ErrorAction Stop
        foreach ($field in @('Status', 'Mode', 'Iteration', 'Seed', 'Filter', 'Tests')) {
            if ($order.PSObject.Properties.Name -notcontains $field) {
                throw "Probe test-order field '$field' is missing."
            }
        }
        if ($order.Mode -ne $result.Mode -or
            [int]$order.Iteration -ne [int]$result.Iteration -or
            [int]$order.Seed -ne [int]$result.Seed -or
            [string]::IsNullOrWhiteSpace([string]$order.Filter) -or
            $order.Filter -ne $result.Filter) {
            throw 'Probe test-order manifest is inconsistent with the result.'
        }
        if ($order.Status -notin @('Selection', 'Captured')) {
            throw "Probe test-order manifest has invalid status '$($order.Status)'."
        }
        $tests = @($order.Tests)
        if ($tests.Count -le 0) {
            throw 'Probe test-order manifest must contain at least one selection or executed test.'
        }
        for ($index = 0; $index -lt $tests.Count; $index++) {
            if ([int]$tests[$index].Ordinal -ne ($index + 1) -or
                [string]::IsNullOrWhiteSpace([string]$tests[$index].Name)) {
                throw 'Probe test-order manifest has an invalid ordinal or test name.'
            }
        }
        if ($order.Status -eq 'Captured' -and [int]$result.TotalCount -gt 0 -and
            $tests.Count -ne [int]$result.TotalCount) {
            throw 'Captured probe test-order manifest does not match the reported test total.'
        }

        if ($result.ReproductionStatus -eq 'Reproduced') {
            if ($result.Mode -ne 'test-contamination' -or $result.Status -ne 'Failed') {
                throw 'Reproduced is valid only for a failed test-contamination execution.'
            }
            if ([bool]$result.TimedOut -or [bool]$result.ControllerTimedOut -or [bool]$result.CleanupTimedOut -or
                -not [string]::IsNullOrWhiteSpace([string]$result.CleanupError)) {
                throw 'Reproduced evidence cannot be timed out or have cleanup failures.'
            }
            if ($null -eq $result.ExitCode -or [int]$result.ExitCode -eq 0) {
                throw 'Reproduced evidence requires a nonzero child exit code.'
            }
            if ($order.Status -ne 'Captured') {
                throw 'Reproduced evidence requires a captured test-order manifest.'
            }

            $reproducedTrx = Read-SmartGuardTrxResult -Path ([string]$result.TrxPath) -ExitCode ([int]$result.ExitCode)
            if ($reproducedTrx.Result -ne 'Failed' -or [int]$reproducedTrx.TotalCount -le 0 -or
                [int]$reproducedTrx.FailedCount -le 0) {
                throw 'Reproduced evidence requires a non-empty failed TRX with at least one failed test.'
            }
            $reproductionOutput = (@($result.StdoutPath, $result.StderrPath) | ForEach-Object {
                if (Test-Path -LiteralPath ([string]$_)) {
                    [System.IO.File]::ReadAllText(
                        [string]$_,
                        [System.Text.UTF8Encoding]::new($false, $true))
                }
            }) -join [Environment]::NewLine
            if ($reproductionOutput -notmatch 'paged-queued') {
                throw 'Reproduced evidence requires the paged-queued failure signature in child output.'
            }
        }

        if ($result.Status -eq 'Passed') {
            if ($result.ReproductionStatus -ne 'NotReproduced') {
                throw 'A passed probe execution must remain explicitly not-reproduced.'
            }
            if ($null -eq $result.ExitCode -or [int]$result.ExitCode -ne 0) {
                throw 'A passed probe result requires child exit code 0.'
            }
            if ($result.TrxStatus -ne 'Passed' -or [bool]$result.TimedOut -or [bool]$result.CleanupTimedOut -or
                -not [string]::IsNullOrWhiteSpace([string]$result.CleanupError)) {
                throw 'A passed probe result requires a completed non-timeout TRX.'
            }

            $trxResult = Read-SmartGuardTrxResult -Path ([string]$result.TrxPath) -ExitCode ([int]$result.ExitCode)
            if ($trxResult.Result -ne 'Passed' -or [int]$trxResult.TotalCount -ne [int]$result.TotalCount) {
                throw "Probe result TRX revalidation failed: $($trxResult.Error)"
            }

            if ($order.Status -ne 'Captured' -or $tests.Count -le 0 -or
                $tests.Count -ne [int]$result.TotalCount) {
                throw 'A passed probe result requires a non-empty test-order manifest matching TRX total.'
            }
        }
        if ($result.Status -eq 'Blocked' -and [string]::IsNullOrWhiteSpace([string]$result.BlockedReason)) {
            throw 'A blocked probe result requires BlockedReason.'
        }

        $result | Add-Member -NotePropertyName ResultPath -NotePropertyValue $Path -Force
        return $result
    }
    catch {
        $failed.Error = "Unable to validate WPF stability probe result '$Path': $($_.Exception.Message)"
        return [pscustomobject]$failed
    }
}

function Get-SmartGuardWpfStabilityProbeExitCode {
    param(
        [Parameter(Mandatory = $true)]
        [psobject[]]$Results,

        [switch]$RequireReproduction
    )

    if (@($Results).Count -eq 0) {
        return 1
    }

    $healthFailures = @($Results | Where-Object {
        $result = if ($_.PSObject.Properties.Name -contains 'Result') { $_.Result } else { $null }
        $resultProperties = if ($null -eq $result) { @() } else { @($result.PSObject.Properties.Name) }
        $resultStatus = if ($resultProperties -contains 'Status') { [string]$result.Status } else { 'Failed' }
        $reproductionStatus = if ($resultProperties -contains 'ReproductionStatus') { [string]$result.ReproductionStatus } else { '' }
        $timedOut = if ($resultProperties -contains 'TimedOut') { [bool]$result.TimedOut } else { $true }
        $controllerTimedOut = if ($_.PSObject.Properties.Name -contains 'ControllerTimedOut') { [bool]$_.ControllerTimedOut } else { $true }
        $controllerEvidenceError = if ($_.PSObject.Properties.Name -contains 'ControllerEvidenceError') { [string]$_.ControllerEvidenceError } else { 'missing' }
        $exitCode = if ($resultProperties -contains 'ExitCode') { $result.ExitCode } else { $null }
        $trxStatus = if ($resultProperties -contains 'TrxStatus') { [string]$result.TrxStatus } else { '' }
        $cleanupTimedOut = if ($resultProperties -contains 'CleanupTimedOut') { [bool]$result.CleanupTimedOut } else { $true }
        $cleanupError = if ($resultProperties -contains 'CleanupError') { [string]$result.CleanupError } else { 'missing' }
        $innerControllerTimedOut = if ($resultProperties -contains 'ControllerTimedOut') { [bool]$result.ControllerTimedOut } else { $true }
        $expectedReproduction = $RequireReproduction -and
            $resultStatus -eq 'Failed' -and
            $reproductionStatus -eq 'Reproduced' -and
            -not $timedOut -and
            -not $controllerTimedOut -and
            -not $innerControllerTimedOut -and
            $null -ne $exitCode -and
            [int]$exitCode -ne 0 -and
            $trxStatus -eq 'Failed' -and
            $_.ControllerExitCode -eq 0 -and
            $_.CaptureStatus -eq 'Passed' -and
            [string]::IsNullOrWhiteSpace($controllerEvidenceError) -and
            -not $cleanupTimedOut -and
            [string]::IsNullOrWhiteSpace($cleanupError)
        (-not $expectedReproduction) -and (
            $_.ControllerExitCode -ne 0 -or
            $controllerTimedOut -or
            $innerControllerTimedOut -or
            -not [string]::IsNullOrWhiteSpace($controllerEvidenceError) -or
            $resultStatus -ne 'Passed' -or
            $_.CaptureStatus -ne 'Passed')
    })
    if ($healthFailures.Count -ne 0) {
        return 1
    }
    if ($RequireReproduction -and @($Results | Where-Object {
        $result = if ($_.PSObject.Properties.Name -contains 'Result') { $_.Result } else { $null }
        $reproductionStatus = if ($null -ne $result -and $result.PSObject.Properties.Name -contains 'ReproductionStatus') {
            [string]$result.ReproductionStatus
        }
        else {
            ''
        }
        $reproductionStatus -ne 'Reproduced'
    }).Count -ne 0) {
        return 2
    }
    return 0
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
    $batchDeadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
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
            DeadlineUtc = $batchDeadlineUtc
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
