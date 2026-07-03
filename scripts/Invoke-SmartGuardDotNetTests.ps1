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

        $output = dotnet build $project --nologo -v q 2>&1
        $output | Write-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for $project"
        }
    }
}

function Invoke-SmartGuardDotNetTestProject {
    param(
        [string]$ProjectPath,
        [string]$Filter,
        [switch]$NoBuild
    )

    $args = @('test', $ProjectPath, '--nologo', '-v', 'q')
    if ($Filter) {
        $args += @('--filter', $Filter)
    }
    if ($NoBuild) {
        $args += '--no-build'
    }

    $output = dotnet @args 2>&1
    return [pscustomobject]@{
        Project  = $ProjectPath
        ExitCode = $LASTEXITCODE
        Output   = ($output | Out-String)
    }
}

function Invoke-SmartGuardDotNetTestJobs {
    param(
        [string[]]$ProjectPaths,
        [string]$Filter,
        [switch]$NoBuild
    )

    $jobs = @()
    foreach ($project in $ProjectPaths) {
        $jobs += Start-Job -ScriptBlock {
            param($ProjectPath, $Filter, $NoBuild)
            $args = @('test', $ProjectPath, '--nologo', '-v', 'q')
            if ($Filter) {
                $args += @('--filter', $Filter)
            }
            if ($NoBuild) {
                $args += '--no-build'
            }

            $output = & dotnet @args 2>&1 | Out-String
            [pscustomobject]@{
                Project  = $ProjectPath
                ExitCode = $LASTEXITCODE
                Output   = $output
            }
        } -ArgumentList $project, $Filter, [bool]$NoBuild
    }

    return $jobs
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

    $parallelJobs = Invoke-SmartGuardDotNetTestJobs -ProjectPaths $parallelProjects -Filter $filter -NoBuild
    $parallelResults = $parallelJobs | Wait-Job | Receive-Job
    $parallelJobs | Remove-Job -Force
    if (-not (Write-SmartGuardDotNetTestResults -Results $parallelResults)) {
        return $false
    }

    $serialResults = @()
    foreach ($project in $serialProjects) {
        $serialResults += Invoke-SmartGuardDotNetTestProject -ProjectPath $project -Filter $filter -NoBuild
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

    if (-not (Test-IsProcessElevated)) {
        return $false
    }

    return $true
}
