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
    param([string]$RepoRoot)

    @(
        (Join-Path $RepoRoot 'Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Cli.Tests\SmartGuard.Cli.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Tray.Tests\SmartGuard.Tray.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Settings.LogView.Tests\SmartGuard.Settings.LogView.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Packaging.Tests\SmartGuard.Packaging.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Engine.PerformanceTests\SmartGuard.Engine.PerformanceTests.csproj')
    )
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
    param([string]$RepoRoot)

    @(
        (Join-Path $RepoRoot 'Tests\SmartGuard.Tray.Tests\SmartGuard.Tray.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Settings.LogView.Tests\SmartGuard.Settings.LogView.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj')
        (Join-Path $RepoRoot 'Tests\SmartGuard.Engine.PerformanceTests\SmartGuard.Engine.PerformanceTests.csproj')
    )
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
        [int]$TimeoutMilliseconds = 5000
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

function Invoke-SmartGuardDotNetTestProcesses {
    param(
        [string[]]$ProjectPaths,
        [string]$Filter,
        [switch]$NoBuild
    )

    $resultDirectory = Join-Path $PSScriptRoot '..\.work\test-results'
    [void](New-Item -ItemType Directory -Path $resultDirectory -Force)
    $processes = @()
    foreach ($project in $ProjectPaths) {
        $arguments = @('test', $project, '--nologo', '-v', 'q')
        if ($Filter) {
            $arguments += @('--filter', $Filter)
        }
        if ($NoBuild) {
            $arguments += '--no-build'
        }

        $argumentText = (($arguments | ForEach-Object {
            '"{0}"' -f $_.Replace('"', '\"')
        }) -join ' ')
        $runId = [guid]::NewGuid().ToString('N')
        $stdoutPath = Join-Path $resultDirectory "dotnet-test-$runId.stdout.log"
        $exitPath = Join-Path $resultDirectory "dotnet-test-$runId.exit"
        $command = "dotnet $argumentText > `"$stdoutPath`" 2>&1 & echo %ERRORLEVEL% > `"$exitPath`""
        $process = Start-Process -FilePath $env:ComSpec -ArgumentList @('/d', '/c', $command) -PassThru
        $processes += [pscustomobject]@{
            Process = $process
            Project = $project
            StdoutPath = $stdoutPath
            ExitPath = $exitPath
        }
    }

    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    $pending = @($processes)
    $results = @()
    while ($pending.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) {
        $completed = @($pending | Where-Object { Test-Path -LiteralPath $_.ExitPath })
        foreach ($entry in $completed) {
            $entry.Process.WaitForExit()
            $exitCode = [int]([System.IO.File]::ReadAllText($entry.ExitPath, [System.Text.Encoding]::UTF8).Trim())
            $output = Read-SmartGuardTextFileWhenUnlocked -Path $entry.StdoutPath
            $results += [pscustomobject]@{
                Project = $entry.Project
                ExitCode = $exitCode
                Output = $output
            }
        }
        $pending = @($pending | Where-Object { -not (Test-Path -LiteralPath $_.ExitPath) })
        if ($pending.Count -gt 0) {
            Start-Sleep -Milliseconds 250
        }
    }

    foreach ($entry in $pending) {
        Stop-Process -Id $entry.Process.Id -Force -ErrorAction SilentlyContinue
        $results += [pscustomobject]@{
            Project = $entry.Project
            ExitCode = 1
            Output = "dotnet test timed out after 10 minutes (PID $($entry.Process.Id))."
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
    }

    $failed = @($Results | Where-Object { $_.ExitCode -ne 0 })
    if ($failed.Count -gt 0) {
        foreach ($result in $failed) {
            $name = Split-Path -Leaf $result.Project
            Write-Host "dotnet test ($name) FAILED" -ForegroundColor Red
        }
        return $false
    }

    return $true
}

function Invoke-SmartGuardDotNetTestSuite {
    param(
        [string]$RepoRoot,
        [string]$Tier = (Get-SmartGuardTestTier)
    )

    $projects = Get-SmartGuardDotNetTestProjectPaths -RepoRoot $RepoRoot
    $parallelProjects = Get-SmartGuardParallelDotNetTestProjectPaths -RepoRoot $RepoRoot
    $serialProjects = Get-SmartGuardSerialDotNetTestProjectPaths -RepoRoot $RepoRoot
    $filter = Get-SmartGuardDotNetTestFilter -Tier $Tier

    Ensure-SmartGuardEnginePublished -RepoRoot $RepoRoot
    Ensure-SmartGuardDotNetTestBuild -RepoRoot $RepoRoot -ProjectPaths $projects

    $parallelResults = Invoke-SmartGuardDotNetTestProcesses -ProjectPaths $parallelProjects -Filter $filter -NoBuild
    if (-not (Write-SmartGuardDotNetTestResults -Results $parallelResults)) {
        return $false
    }

    $serialResults = @()
    foreach ($project in $serialProjects) {
        $serialResults += Invoke-SmartGuardDotNetTestProcesses -ProjectPaths @($project) -Filter $filter -NoBuild
    }

    return Write-SmartGuardDotNetTestResults -Results $serialResults
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
