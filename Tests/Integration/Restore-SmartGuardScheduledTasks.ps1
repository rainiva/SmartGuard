#Requires -Version 5.1
<#
  Capture / restore SmartGuard global scheduled tasks for integration test isolation.
  Dot-source friendly; restore requires an elevated session (no automatic UAC).
#>

$script:SmartGuardScheduledTasksRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $script:SmartGuardScheduledTasksRepoRoot 'scripts\Test-IsProcessElevated.ps1')
. (Join-Path $PSScriptRoot 'SmartGuardStop.ps1')

$script:SmartGuardGuardianTaskName = 'SmartGuard Guardian'
$script:SmartGuardTrayTaskName = 'SmartGuard Tray'

$script:PoisonRootMarkers = @(
    'SmartGuardUninstallTest',
    'sg-installer-ui-',
    'sg-user-flow-'
)

function Resolve-SmartGuardScheduledTasksRepoRoot {
    if ($global:SG_TestRepoRoot) { return $global:SG_TestRepoRoot }
    if ($global:SG_UI_RepoRoot) { return $global:SG_UI_RepoRoot }
    return $script:SmartGuardScheduledTasksRepoRoot
}

function Test-SmartGuardTaskRootIsPoison {
    param([string]$Root)

    if ([string]::IsNullOrWhiteSpace($Root)) {
        return $false
    }

    foreach ($marker in $script:PoisonRootMarkers) {
        if ($Root.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Get-SmartGuardRootFromTaskCommand {
    param(
        [string]$Command,
        [string]$Arguments
    )

    $combined = "$(if ($Command) { $Command }) $(if ($Arguments) { $Arguments })".Trim()
    if ([string]::IsNullOrWhiteSpace($combined)) {
        return $null
    }

    if ($combined -match '--root\s+"([^"]+)"') {
        return $Matches[1]
    }

    if ($combined -match "--root\s+'([^']+)'") {
        return $Matches[1]
    }

    if ($combined -match '--root\s+(\S+)') {
        return $Matches[1].Trim('"', "'")
    }

    return $null
}

function Get-SmartGuardScheduledTaskSlot {
    param([string]$TaskName)

    $xmlText = $null
    try {
        # Route native stderr away so missing tasks don't spam NativeCommandError in Pester transcripts.
        $xmlText = cmd.exe /c "schtasks.exe /Query /TN `"$TaskName`" /XML /HRESULT 2>nul" | Out-String
    }
    catch {
        throw "Unable to query scheduled task '$TaskName': $($_.Exception.Message)"
    }

    # HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND) is the only confirmed absent slot.
    if ($LASTEXITCODE -eq -2147024894) {
        return 'Missing'
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to query scheduled task '$TaskName': exit $LASTEXITCODE. Baseline is unknown."
    }
    if ([string]::IsNullOrWhiteSpace($xmlText)) {
        throw "Scheduled task '$TaskName' query returned empty XML. Baseline is unknown."
    }

    try {
        [xml]$doc = $xmlText.Trim()
    }
    catch {
        throw "Scheduled task '$TaskName' query returned invalid XML: $($_.Exception.Message)"
    }

    $exec = $doc.SelectSingleNode('//*[local-name()="Exec"]')
    if (-not $exec) {
        throw "Scheduled task '$TaskName' has no executable action. Baseline cannot be restored."
    }

    $commandNode = $exec.SelectSingleNode('*[local-name()="Command"]')
    $argumentsNode = $exec.SelectSingleNode('*[local-name()="Arguments"]')
    $workingDirectoryNode = $exec.SelectSingleNode('*[local-name()="WorkingDirectory"]')

    $command = if ($commandNode) { [string]$commandNode.InnerText } else { '' }
    $arguments = if ($argumentsNode) { [string]$argumentsNode.InnerText } else { '' }
    $workingDirectory = if ($workingDirectoryNode) { [string]$workingDirectoryNode.InnerText } else { '' }

    $fullCommand = "$(if ($command) { $command }) $(if ($arguments) { $arguments })".Trim()
    $root = Get-SmartGuardRootFromTaskCommand -Command $command -Arguments $arguments
    if ([string]::IsNullOrWhiteSpace($command) -or [string]::IsNullOrWhiteSpace($root)) {
        throw "Scheduled task '$TaskName' has no recoverable executable/root. Baseline is unknown."
    }

    return [PSCustomObject]@{
        Command            = $fullCommand
        WorkingDirectory   = $workingDirectory
        Root               = $root
    }
}

function Test-SmartGuardScheduledTaskSlotPresent {
    param($Slot)

    return (
        $null -ne $Slot -and
        $Slot -ne 'Missing' -and
        -not [string]::IsNullOrWhiteSpace([string]$Slot.Root))
}

function Test-SmartGuardTaskBackedProcessRunning {
    param(
        $Slot,
        [string]$ProcessName,
        [string]$ExecutableName
    )

    if (-not (Test-SmartGuardScheduledTaskSlotPresent -Slot $Slot) -or
        (Test-SmartGuardTaskRootIsPoison -Root ([string]$Slot.Root))) {
        return $false
    }

    $expectedPath = [System.IO.Path]::GetFullPath(
        (Join-Path ([string]$Slot.Root) "bin\$ExecutableName"))
    foreach ($process in @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
        $processPath = ''
        try {
            $processPath = [string]$process.Path
        }
        catch {
            continue
        }

        if ([string]::IsNullOrWhiteSpace($processPath)) {
            continue
        }

        try {
            $resolvedProcessPath = [System.IO.Path]::GetFullPath($processPath)
        }
        catch {
            continue
        }

        if ($resolvedProcessPath.Equals(
                $expectedPath,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Capture-SmartGuardScheduledTasks {
    $guardian = Get-SmartGuardScheduledTaskSlot -TaskName $script:SmartGuardGuardianTaskName
    $tray = Get-SmartGuardScheduledTaskSlot -TaskName $script:SmartGuardTrayTaskName

    foreach ($slot in @($guardian, $tray)) {
        if ($slot -eq 'Missing') {
            continue
        }

        if (Test-SmartGuardTaskRootIsPoison -Root $slot.Root) {
            throw "Refuse to capture poisoned baseline: scheduled task root '$($slot.Root)' matches a test temporary path pattern. Restore your production SmartGuard install before running integration tests."
        }

        if ($slot.Command -and (Test-SmartGuardTaskRootIsPoison -Root $slot.Command)) {
            throw "Refuse to capture poisoned baseline: scheduled task command references a test temporary path. Restore your production SmartGuard install before running integration tests."
        }

        if ($slot.WorkingDirectory -and (Test-SmartGuardTaskRootIsPoison -Root $slot.WorkingDirectory)) {
            throw "Refuse to capture poisoned baseline: scheduled task working directory '$($slot.WorkingDirectory)' matches a test temporary path pattern. Restore your production SmartGuard install before running integration tests."
        }
    }

    return [PSCustomObject]@{
        Guardian          = $guardian
        Tray              = $tray
        GuardianWasRunning = Test-SmartGuardTaskBackedProcessRunning `
            -Slot $guardian `
            -ProcessName 'SmartGuard.Engine' `
            -ExecutableName 'SmartGuard.Engine.exe'
        TrayWasRunning     = Test-SmartGuardTaskBackedProcessRunning `
            -Slot $tray `
            -ProcessName 'SmartGuard.Tray' `
            -ExecutableName 'SmartGuard.Tray.exe'
    }
}

function Resolve-SmartGuardRestoreRootFromSnapshot {
    param($Snapshot)

    $roots = @()
    foreach ($slotName in @('Guardian', 'Tray')) {
        $slot = $Snapshot.$slotName
        if ($slot -ne 'Missing' -and -not [string]::IsNullOrWhiteSpace($slot.Root)) {
            $roots += $slot.Root
        }
    }

    if ($roots.Count -eq 0) {
        return $null
    }

    $distinct = @($roots | Select-Object -Unique)
    if ($distinct.Count -gt 1) {
        Write-Warning "SmartGuard scheduled task snapshot has multiple roots ($($distinct -join ', ')); using '$($distinct[0])' for restore."
    }

    return [string]$distinct[0]
}

function Invoke-SmartGuardNativeExe {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$ArgumentList,
        [int]$TimeoutSec = 120
    )

    if (-not (Test-Path -LiteralPath $FilePath)) {
        throw "Executable not found: $FilePath"
    }

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    try {
        $quotedArguments = @($ArgumentList | ForEach-Object {
            ConvertTo-SmartGuardQuotedProcessArgument -Value $_
        })
        $proc = Start-Process -FilePath $FilePath `
            -ArgumentList $quotedArguments `
            -PassThru `
            -NoNewWindow `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath

        $timeoutMs = [Math]::Max(1, $TimeoutSec) * 1000
        if (-not $proc.WaitForExit($timeoutMs)) {
            try { $proc.Kill() } catch { /* ignore */ }
            throw "Timed out after ${TimeoutSec}s waiting for '$FilePath' $($ArgumentList -join ' ')."
        }

        $stdout = ''
        $stderr = ''
        if (Test-Path -LiteralPath $stdoutPath) {
            $stdout = [System.IO.File]::ReadAllText($stdoutPath).Trim()
        }
        if (Test-Path -LiteralPath $stderrPath) {
            $stderr = [System.IO.File]::ReadAllText($stderrPath).Trim()
        }

        return [PSCustomObject]@{
            ExitCode = [int]$proc.ExitCode
            StdOut   = $stdout
            StdErr   = $stderr
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-SmartGuardRestoreInstall {
    param(
        [string]$Root,
        [string]$EngineExe,
        [int]$MaxAttempts = 3
    )

    # Kill live Engine/Tray so schtasks re-registration is not racing a running instance.
    Stop-SmartGuardProcessesWithoutElevation
    Start-Sleep -Milliseconds 400

    $previousSkip = $env:SMARTGUARD_SKIP_SCHEDULED_TASKS
    try {
        Remove-Item Env:SMARTGUARD_SKIP_SCHEDULED_TASKS -ErrorAction SilentlyContinue

        $last = $null
        for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
            $last = Invoke-SmartGuardNativeExe -FilePath $EngineExe -ArgumentList @(
                '--root', $Root,
                '--install',
                '--skip-publish'
            )

            if ($last.ExitCode -eq 0) {
                return
            }

            # Verify by task slot: some hosts report a spurious -1 even after successful schtasks write.
            $guardian = Get-SmartGuardScheduledTaskSlot -TaskName $script:SmartGuardGuardianTaskName
            if ($guardian -ne 'Missing' -and $guardian.Root -and
                ($guardian.Root.TrimEnd('\') -eq $Root.TrimEnd('\'))) {
                Write-Warning "Restore install exit $($last.ExitCode) but Guardian already points at '$Root'; treating as success."
                return
            }

            Write-Warning "Restore install attempt $attempt/$MaxAttempts failed (exit $($last.ExitCode)) for root '$Root'."
            Stop-SmartGuardProcessesWithoutElevation
            Start-Sleep -Seconds 1
        }

        $detail = @()
        if ($last.StdOut) { $detail += "stdout: $($last.StdOut)" }
        if ($last.StdErr) { $detail += "stderr: $($last.StdErr)" }
        $extra = if ($detail.Count -gt 0) { ' ' + ($detail -join ' | ') } else { '' }
        throw "Restore install failed with exit code $($last.ExitCode) for root '$Root'.$extra"
    }
    finally {
        if ($null -eq $previousSkip) {
            Remove-Item Env:SMARTGUARD_SKIP_SCHEDULED_TASKS -ErrorAction SilentlyContinue
        }
        else {
            $env:SMARTGUARD_SKIP_SCHEDULED_TASKS = $previousSkip
        }
    }
}

function Remove-SmartGuardScheduledTaskSlot {
    param([string]$TaskName)

    if ((Get-SmartGuardScheduledTaskSlot -TaskName $TaskName) -eq 'Missing') {
        return
    }

    $schtasksExe = Join-Path $env:SystemRoot 'System32\schtasks.exe'
    $result = Invoke-SmartGuardNativeExe `
        -FilePath $schtasksExe `
        -ArgumentList @('/Delete', '/TN', $TaskName, '/F')
    if ($result.ExitCode -ne 0 -and
        (Get-SmartGuardScheduledTaskSlot -TaskName $TaskName) -ne 'Missing') {
        throw "Restore delete task '$TaskName' failed with exit code $($result.ExitCode). $($result.StdErr)"
    }
}

function Start-SmartGuardRestoredTasks {
    param(
        [string]$Root,
        $Snapshot
    )

    $tasksToStart = @(
        if ((Test-SmartGuardScheduledTaskSlotPresent -Slot $Snapshot.Guardian) -and
            [bool]$Snapshot.GuardianWasRunning) {
            [PSCustomObject]@{
                CliName = 'guardian'
                TaskName = $script:SmartGuardGuardianTaskName
            }
        }
        if ((Test-SmartGuardScheduledTaskSlotPresent -Slot $Snapshot.Tray) -and
            [bool]$Snapshot.TrayWasRunning) {
            [PSCustomObject]@{
                CliName = 'tray'
                TaskName = $script:SmartGuardTrayTaskName
            }
        }
    )
    if ($tasksToStart.Count -eq 0) {
        return
    }

    $cliCandidates = @(
        (Join-Path $Root 'bin\SmartGuard.Cli.exe'),
        (Join-Path (Resolve-SmartGuardScheduledTasksRepoRoot) 'bin\SmartGuard.Cli.exe')
    )

    foreach ($cliExe in $cliCandidates) {
        if (-not (Test-Path -LiteralPath $cliExe)) {
            continue
        }

        foreach ($task in $tasksToStart) {
            $start = Invoke-SmartGuardNativeExe `
                -FilePath $cliExe `
                -ArgumentList @('start', $task.CliName, '--root', $Root)
            if ($start.ExitCode -ne 0) {
                throw "Restore start $($task.CliName) failed with exit code $($start.ExitCode) for root '$Root'. $($start.StdErr)"
            }
        }

        return
    }

    $schtasksExe = Join-Path $env:SystemRoot 'System32\schtasks.exe'
    foreach ($task in $tasksToStart) {
        $runTask = Invoke-SmartGuardNativeExe `
            -FilePath $schtasksExe `
            -ArgumentList @('/Run', '/TN', $task.TaskName)
        if ($runTask.ExitCode -ne 0) {
            throw "Restore schtasks /Run '$($task.TaskName)' failed with exit code $($runTask.ExitCode)."
        }
    }
}

function Invoke-SmartGuardEngineUninstall {
    param(
        [string]$EngineExe,
        [string]$Root
    )

    Stop-SmartGuardProcessesWithoutElevation
    Start-Sleep -Milliseconds 400

    $result = Invoke-SmartGuardNativeExe -FilePath $EngineExe -ArgumentList @(
        '--root', $Root,
        '--uninstall'
    )
    if ($result.ExitCode -ne 0) {
        throw "Restore uninstall failed with exit code $($result.ExitCode) for root '$Root'. $($result.StdErr)"
    }
}

function Restore-SmartGuardScheduledTasks {
    param(
        $Snapshot,
        [bool]$StartProcesses = $true
    )

    if (-not $Snapshot) {
        throw 'Restore-SmartGuardScheduledTasks requires a Capture snapshot.'
    }

    if (-not (Test-IsProcessElevated)) {
        throw 'Restore-SmartGuardScheduledTasks requires an elevated PowerShell session. Re-run the test suite as administrator or manually run install --root <production> before retrying.'
    }

    $restoreRoot = Resolve-SmartGuardRestoreRootFromSnapshot -Snapshot $Snapshot
    $repoRoot = Resolve-SmartGuardScheduledTasksRepoRoot
    $repoEngineExe = Join-Path $repoRoot 'bin\SmartGuard.Engine.exe'

    if (-not (Test-Path -LiteralPath $repoEngineExe)) {
        throw "Restore requires SmartGuard.Engine.exe at '$repoEngineExe'. Run build.cmd first."
    }

    if ($null -eq $restoreRoot) {
        Invoke-SmartGuardEngineUninstall -EngineExe $repoEngineExe -Root $repoRoot
        return
    }

    $rootEngineExe = Join-Path $restoreRoot 'bin\SmartGuard.Engine.exe'
    if (-not (Test-Path -LiteralPath $restoreRoot) -or -not (Test-Path -LiteralPath $rootEngineExe)) {
        Write-Warning "Restore snapshot root '$restoreRoot' is missing or incomplete; deleting global scheduled tasks via uninstall."
        Invoke-SmartGuardEngineUninstall -EngineExe $repoEngineExe -Root $repoRoot
        return
    }

    # Prefer repo Engine so restore always uses the build under test (production root may lag).
    Invoke-SmartGuardRestoreInstall -Root $restoreRoot -EngineExe $repoEngineExe

    if (-not (Test-SmartGuardScheduledTaskSlotPresent -Slot $Snapshot.Guardian)) {
        Remove-SmartGuardScheduledTaskSlot -TaskName $script:SmartGuardGuardianTaskName
    }
    if (-not (Test-SmartGuardScheduledTaskSlotPresent -Slot $Snapshot.Tray)) {
        Remove-SmartGuardScheduledTaskSlot -TaskName $script:SmartGuardTrayTaskName
    }

    # Per-It restore should re-register only; starting Engine/Tray races the next Inno PrepareToInstall.
    if ($StartProcesses -and
        ([bool]$Snapshot.GuardianWasRunning -or [bool]$Snapshot.TrayWasRunning)) {
        Start-SmartGuardRestoredTasks -Root $restoreRoot -Snapshot $Snapshot
    }
}

function Assert-SmartGuardSkipInstallHonored {
    param(
        [string]$InstallRoot,
        $Snapshot
    )

    if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
        throw 'Assert-SmartGuardSkipInstallHonored requires InstallRoot.'
    }

    if (-not $Snapshot) {
        throw 'Assert-SmartGuardSkipInstallHonored requires the Capture snapshot.'
    }

    $startup = Join-Path $InstallRoot 'SmartGuard.startup.log'
    if (-not (Test-Path -LiteralPath $startup)) {
        throw "Skip install startup log not found at $startup"
    }

    $text = [System.IO.File]::ReadAllText($startup, [System.Text.UTF8Encoding]::new($false))
    if ($text -notlike '*skipping scheduled task registration and desktop shell*') {
        throw "Skip install did not log skipping message at $startup"
    }

    # After Capture→Stop, live tasks may already be Missing (Stop/uninstall clears them).
    # Skip install must not recreate them at InstallRoot; do not require equality to pre-Stop snapshot.
    $normalizedInstallRoot = $InstallRoot.TrimEnd('\')

    foreach ($entry in @(
            @{ Name = 'Guardian'; TaskName = $script:SmartGuardGuardianTaskName }
            @{ Name = 'Tray'; TaskName = $script:SmartGuardTrayTaskName }
        )) {
        $current = Get-SmartGuardScheduledTaskSlot -TaskName $entry.TaskName
        if ($current -eq 'Missing') {
            continue
        }

        if ($current.Command -like "*$normalizedInstallRoot*" -or
            $current.WorkingDirectory -like "*$normalizedInstallRoot*") {
            throw "Skip install violated isolation: $($entry.Name) task was repointed to test install root '$InstallRoot'."
        }

        if (($current.Root -and (Test-SmartGuardTaskRootIsPoison -Root $current.Root)) -or
            (Test-SmartGuardTaskRootIsPoison -Root $current.Command) -or
            (Test-SmartGuardTaskRootIsPoison -Root $current.WorkingDirectory)) {
            throw "Skip install violated isolation: $($entry.Name) task points at a test temp root. Command='$($current.Command)' Root='$($current.Root)'."
        }
    }
}
