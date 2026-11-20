Describe 'SmartGuard' {
    Describe 'Acceptance status read fault boundaries' {
        It 'handles <Case> without accepting unavailable or invalid evidence' -ForEach @(
            @{Case='SharedWriter'; Success=$true},
            @{Case='TemporaryLock'; Success=$true},
            @{Case='AtomicPublish'; Success=$true},
            @{Case='PermanentLock'; Success=$false},
            @{Case='Corrupt'; Success=$false},
            @{Case='Stale'; Success=$false},
            @{Case='Future'; Success=$false},
            @{Case='WrongPid'; Success=$false},
            @{Case='DC'; Success=$false},
            @{Case='Paused'; Success=$false},
            @{Case='Oversize'; Success=$false}
        ) {
            $root=Split-Path -Parent $PSScriptRoot
            $pwsh=Get-Command pwsh.exe -ErrorAction Stop
            $fixtureRoot=Join-Path $TestDrive $Case
            & $pwsh.Source -NoProfile -File (Join-Path $PSScriptRoot 'Integration/AcceptanceStatusReader.Fixture.ps1') -ReaderPath (Join-Path $root 'scripts/Read-SmartGuardAcceptanceStatus.ps1') -BinDirectory (Join-Path $root 'bin') -Root $fixtureRoot -Case $Case | Out-Null
            $LASTEXITCODE | Should -Be 0
            $result=Get-Content (Join-Path $fixtureRoot 'result.json') -Raw -Encoding UTF8|ConvertFrom-Json
            $result.Success | Should -Be $Success
            $result.ElapsedMs | Should -BeLessThan 2000
            if($Success){$result.Pid|Should -Be 123; $result.Attempts|Should -BeLessOrEqual 6}
            else{$result.Error|Should -Not -BeNullOrEmpty}
        }
    }
    Describe 'Power lifecycle acceptance evidence' {
        It 'classifies <Case> without losing checks to an exception' -ForEach @(
            @{Case='Complete'; State='Passed'; Code=0; Check='lifecycle-events'; CheckState='Passed'},
            @{Case='NarrowQuery'; State='Passed'; Code=0; Check='windows-suspend-resume-events'; CheckState='Passed'},
            @{Case='MissingLifecycle'; State='Blocked'; Code=2; Check='lifecycle-events'; CheckState='Blocked'},
            @{Case='MissingBaseline'; State='Blocked'; Code=2; Check='pipeline-health-delta'; CheckState='Blocked'},
            @{Case='MissingPost'; State='Blocked'; Code=2; Check='pipeline-health-delta'; CheckState='Blocked'},
            @{Case='MissingPid'; State='Blocked'; Code=2; Check='pipeline-health-delta'; CheckState='Blocked'},
            @{Case='Dropped'; State='Failed'; Code=1; Check='pipeline-health-delta'; CheckState='Failed'}
            @{Case='HibernateComplete'; State='Passed'; Code=0; Check='hibernate-state'; CheckState='Passed'},
            @{Case='HibernateWrongState'; State='Failed'; Code=1; Check='hibernate-state'; CheckState='Failed'},
            @{Case='HibernateMissingState'; State='Blocked'; Code=2; Check='hibernate-state'; CheckState='Blocked'},
            @{Case='RepeatedLifecycle'; State='Passed'; Code=0; Check='lifecycle-events'; CheckState='Passed'},
            @{Case='RepeatedLifecycleWrite'; State='Failed'; Code=1; Check='no-suspend-plan-write'; CheckState='Failed'},
            @{Case='UnclosedLifecycle'; State='Blocked'; Code=2; Check='lifecycle-events'; CheckState='Blocked'}
        ) {
            $root = Split-Path -Parent $PSScriptRoot
            $fixtureRoot = Join-Path $TestDrive $Case
            & (Get-Process -Id $PID).Path -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Integration/PowerLifecycleAcceptance.Fixture.ps1') -ScriptPath (Join-Path $root 'scripts/Invoke-PowerLifecycleAcceptance.ps1') -Root $fixtureRoot -Case $Case | Out-Null
            $LASTEXITCODE | Should -Be $Code
            $result = [IO.File]::ReadAllText((Join-Path $fixtureRoot 'result.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json
            $result.result | Should -Be $State
            $result.error | Should -BeNullOrEmpty
            @($result.checks).Count | Should -Be $(if($Case -like 'Hibernate*'){6}else{5})
            ($result.checks | Where-Object name -eq $Check).status | Should -Be $CheckState
        }
    }
    Describe 'Tray assets' {
        It 'canonical SmartGuard.ico lives under assets branding and official source frames exist' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            $officialDir = Join-Path $root 'assets\branding\official'
            Test-Path -LiteralPath $icon | Should -Be $true
            (Get-Item -LiteralPath $icon).Length | Should -BeGreaterThan 1000
            Test-Path -LiteralPath $officialDir | Should -Be $true

            foreach ($name in @(
                'SmartGuard-final-official-16.png',
                'SmartGuard-final-official-2048-transparent-24.png',
                'SmartGuard-final-official-2048-transparent-32.png',
                'SmartGuard-final-official-2048-transparent-48.png',
                'SmartGuard-final-official-4096-transparent-64.png',
                'SmartGuard-final-official-4096-transparent-128.png',
                'SmartGuard-final-official-4096-transparent-256.png'
            )) {
                Test-Path -LiteralPath (Join-Path $officialDir $name) | Should -Be $true
            }
        }

        It 'canonical SmartGuard.ico matches official source frames and carries promoted shell sizes' {
            $root = Split-Path -Parent $PSScriptRoot
            $icon = Join-Path $root 'assets\branding\SmartGuard.ico'
            $officialDir = Join-Path $root 'assets\branding\official'
            $metricsJson = python -c "from pathlib import Path; from PIL import Image, ImageChops; import json, sys; icon_path=Path(sys.argv[1]); official_dir=Path(sys.argv[2]); ico=Image.open(icon_path); expected={16:'SmartGuard-final-official-16.png',24:'SmartGuard-final-official-2048-transparent-24.png',32:'SmartGuard-final-official-2048-transparent-32.png',48:'SmartGuard-final-official-2048-transparent-48.png',64:'SmartGuard-final-official-4096-transparent-64.png',128:'SmartGuard-final-official-4096-transparent-128.png',256:'SmartGuard-final-official-4096-transparent-256.png'}; result={'sizes': [f'{w}x{h}' for w,h in sorted(list(ico.ico.sizes()))], 'matches': {}}; 
for size, name in expected.items():
    frame = ico.ico.getimage((size,size)).convert('RGBA')
    source = Image.open(official_dir / name).convert('RGBA')
    result['matches'][str(size)] = ImageChops.difference(frame, source).getbbox() is None
print(json.dumps(result))" $icon $officialDir
            $metrics = $metricsJson | ConvertFrom-Json

            $metrics.sizes | Should -Be @(
                '16x16',
                '20x20',
                '24x24',
                '32x32',
                '40x40',
                '48x48',
                '64x64',
                '128x128',
                '256x256'
            )
            foreach ($size in '16', '24', '32', '48', '64', '128', '256') {
                $metrics.matches.$size | Should -Be $true
            }
        }

        It 'retires the old draft asset tree and icon generation scripts' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'assets\icon') | Should -Be $false

            foreach ($path in @(
                'scripts\generate-smartguard-icons.py',
                'scripts\generate-smartguard-small-draft-board.py',
                'scripts\generate-smartguard-guardian-variants.py',
                'scripts\generate-smartguard-cursor-style-drafts.py',
                'scripts\generate-smartguard-cursor-style-hd.py',
                'scripts\test_generate_smartguard_icons.py',
                'scripts\test_generate_smartguard_guardian_variants.py'
            )) {
                Test-Path -LiteralPath (Join-Path $root $path) | Should -Be $false
            }
        }

        It 'does not ship Create-TrayIcon.ps1 and packaging project exists' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'lib\Create-TrayIcon.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\SmartGuard.Packaging.csproj') | Should -Be $true
        }
    }

    Describe 'Phase 7.4 settings xaml source of truth' {
        It 'settings project owns committed SmartGuard.Settings.xaml and retires loose runtime copies' {
            $root = Split-Path -Parent $PSScriptRoot
            $xamlPath = Join-Path $root 'src\SmartGuard.Settings\SmartGuard.Settings.xaml'
            $writer = Join-Path $root 'lib\Write-SmartGuardSettingsXaml.ps1'
            Test-Path -LiteralPath $xamlPath | Should -Be $true
            Test-Path -LiteralPath $writer | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'lib\SmartGuard.Settings.xaml') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'lib\Themes\Light.xaml') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'lib\Themes\Dark.xaml') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'lib\SmartGuard.ico') | Should -Be $false
            $xaml = Get-Content -LiteralPath $xamlPath -Raw -Encoding UTF8
            $xaml | Should -Match 'x:Name="tglPaused"'
            $xaml | Should -Match 'x:Name="tglAutoStart"'
        }

        It 'Settings project uses project-local xaml and themes as WPF pages' {
            $root = Split-Path -Parent $PSScriptRoot
            $csproj = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Settings\SmartGuard.Settings.csproj') -Raw -Encoding UTF8
            $csproj | Should -Match '<Page Include="SmartGuard\.Settings\.xaml"'
            $csproj | Should -Match '<Page Include="Themes\\Light\.xaml"'
            $csproj | Should -Match '<Page Include="Themes\\Dark\.xaml"'
            $csproj | Should -Not -Match 'lib\\SmartGuard\.Settings\.xaml'
            $csproj | Should -Not -Match 'lib\\Themes\\'
        }

        It 'Build-Staging does not require loose settings xaml payloads' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'installer\Build-Staging.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\SmartGuard.Packaging.csproj') | Should -Be $true
        }
    }

    Describe 'WPF test host determinism contract' {
        It 'uses quiescent cleanup without fixed sleeps' {
            $root = Split-Path -Parent $PSScriptRoot
            $hostSource = Get-Content -LiteralPath (
                Join-Path $root 'Tests\SmartGuard.Settings.Tests\WpfStaTestHost.cs'
            ) -Raw -Encoding UTF8

            $hostSource | Should -Not -Match '\bThread\.Sleep\s*\('
            $hostSource | Should -Match '\bAssertQuiescent\s*\('
        }

        It 'WPF stability probe owns an isolated fail-closed controller contract' {
            $root = Split-Path -Parent $PSScriptRoot
            $projectPath = Join-Path $root 'Tests\SmartGuard.Settings.StabilityProbe\SmartGuard.Settings.StabilityProbe.csproj'
            $programPath = Join-Path $root 'Tests\SmartGuard.Settings.StabilityProbe\Program.cs'
            $probePath = Join-Path $root 'scripts\Invoke-WpfStabilityProbe.ps1'

            Test-Path -LiteralPath $projectPath | Should -BeTrue
            Test-Path -LiteralPath $programPath | Should -BeTrue
            Test-Path -LiteralPath $probePath | Should -BeTrue

            $project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
            $program = Get-Content -LiteralPath $programPath -Raw -Encoding UTF8
            $probe = Get-Content -LiteralPath $probePath -Raw -Encoding UTF8

            $project | Should -Match '<OutputType>Exe</OutputType>'
            $program | Should -Match 'test-contamination'
            $program | Should -Match 'production-equivalent'
            $program | Should -Match 'TimedOut'
            $program | Should -Match 'TRX'
            $program | Should -Match 'startInfo\.Arguments'
            $program | Should -Not -Match 'ArgumentList\.Add\(command\)'
            $program | Should -Match 'TestOrderPath'
            $program | Should -Match 'ReproductionStatus'
            $program | Should -Match 'CleanupTimedOut'
            $program | Should -Match 'ControllerTimedOut'
            $program | Should -Match 'SelectionPath'
            $probe | Should -Match 'RequireReproduction'
            $probe | Should -Match '\[string\]\$ToolRoot'
            $probe | Should -Not -Match '\[string\]\$ToolRoot\s*=\s*\(Join-Path \$PSScriptRoot'
            $probe | Should -Match 'IsNullOrWhiteSpace\(\$ToolRoot\)'
            $probe | Should -Match 'function Resolve-WpfStabilityTestHostPid'
            $probe | Should -Match 'function Start-WpfStabilityController'
            $probe | Should -Match 'Start-Process'
            $probe | Should -Match '-FilePath \$dotnetCommand\.Source'
            $probe | Should -Match 'function Start-WpfStabilityDiagnosticCapture'
            $probe | Should -Match 'function Invoke-WpfStabilityTestSelection'
            $probe | Should -Match 'test-selection'
            $probe | Should -Match 'dotnet-counters\.exe'
            $probe | Should -Match 'dotnet-stack\.exe'
            $probe | Should -Match 'dotnet-trace\.exe'
            $probe | Should -Match 'capture-status\.json'
            $probe | Should -Match 'CapturePhase'
            $probe | Should -Match 'CaptureDeadlineUtc'
            $probe | Should -Match 'ControllerTimedOut'
            $probe | Should -Match 'AddSeconds\(2\)'
            $probe | Should -Match 'timeout-pre-terminate'
            $probe | Should -Match 'Unavailable|Blocked'
            $probe | Should -Match 'Read-SmartGuardWpfStabilityProbeResult'
            $probe | Should -Not -Match 'production-equivalent[\s\S]{0,600}test-contamination'
        }

        It 'WPF stability probe result reader rejects missing and malformed evidence' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-wpf-probe-result-' + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                $missing = Read-SmartGuardWpfStabilityProbeResult -Path (Join-Path $tempDirectory 'missing.json')
                $missing.Status | Should -Be 'Failed'

                $malformedPath = Join-Path $tempDirectory 'malformed.json'
                [System.IO.File]::WriteAllText($malformedPath, '{', [System.Text.UTF8Encoding]::new($false))
                $malformed = Read-SmartGuardWpfStabilityProbeResult -Path $malformedPath
                $malformed.Status | Should -Be 'Failed'

                $zeroPath = Join-Path $tempDirectory 'zero.json'
                [System.IO.File]::WriteAllText(
                    $zeroPath,
                    '{"Status":"Passed","TrxStatus":"Passed","TotalCount":0,"TestPid":123,"TimedOut":false}',
                    [System.Text.UTF8Encoding]::new($false))
                $zero = Read-SmartGuardWpfStabilityProbeResult -Path $zeroPath
                $zero.Status | Should -Be 'Failed'

                $nonZeroPath = Join-Path $tempDirectory 'passed-with-nonzero-exit.json'
                $nonZero = [ordered]@{
                    Status = 'Passed';
                    ExecutionStatus = 'Passed';
                    ReproductionStatus = 'NotReproduced';
                    Mode = 'test-contamination';
                    Iteration = 1;
                    Seed = 7;
                    ControllerPid = 123;
                    StartUtc = '2026-08-23T00:00:00Z';
                    EndUtc = '2026-08-23T00:00:01Z';
                    ExitCode = 23;
                    ControllerTimedOut = $false;
                    TimedOut = $false;
                    TrxPath = (Join-Path $tempDirectory 'missing.trx');
                    StdoutPath = (Join-Path $tempDirectory 'stdout.log');
                    StderrPath = (Join-Path $tempDirectory 'stderr.log');
                    Filter = 'self-test';
                    TrxStatus = 'Passed';
                    TotalCount = 1;
                    CleanupTimedOut = $false;
                    CleanupError = $null;
                    TestOrderPath = (Join-Path $tempDirectory 'test-order.json')
                }
                [System.IO.File]::WriteAllText(
                    $nonZeroPath,
                    ($nonZero | ConvertTo-Json),
                    [System.Text.UTF8Encoding]::new($false))
                $nonZeroResult = Read-SmartGuardWpfStabilityProbeResult -Path $nonZeroPath
                $nonZeroResult.Status | Should -Be 'Failed'

                $failedMissingOrderPath = Join-Path $tempDirectory 'failed-missing-order.json'
                $failedMissingOrder = [ordered]@{
                    Status = 'Failed';
                    ExecutionStatus = 'Failed';
                    ReproductionStatus = 'Blocked';
                    Mode = 'test-contamination';
                    Iteration = 1;
                    Seed = 7;
                    Filter = 'self-test';
                    ControllerPid = 123;
                    StartUtc = '2026-08-23T00:00:00Z';
                    EndUtc = '2026-08-23T00:00:01Z';
                    ExitCode = 23;
                    ControllerTimedOut = $false;
                    TimedOut = $false;
                    TrxPath = (Join-Path $tempDirectory 'missing.trx');
                    StdoutPath = (Join-Path $tempDirectory 'stdout.log');
                    StderrPath = (Join-Path $tempDirectory 'stderr.log');
                    TestOrderPath = (Join-Path $tempDirectory 'missing-test-order.json');
                    TrxStatus = 'Missing';
                    TotalCount = 0;
                    CleanupTimedOut = $false;
                    CleanupError = $null;
                }
                [System.IO.File]::WriteAllText(
                    $failedMissingOrderPath,
                    ($failedMissingOrder | ConvertTo-Json),
                    [System.Text.UTF8Encoding]::new($false))
                $failedMissingOrderResult = Read-SmartGuardWpfStabilityProbeResult -Path $failedMissingOrderPath
                $failedMissingOrderResult.Error | Should -Match 'test-order'

                $badReproducedOrderPath = Join-Path $tempDirectory 'bad-reproduced-order.json'
                $badReproducedOrder = [ordered]@{
                    Status = 'Selection';
                    Mode = 'test-contamination';
                    Iteration = 1;
                    Seed = 7;
                    Filter = 'self-test';
                    Tests = @([ordered]@{ Ordinal = 1; Name = 'self-test' });
                }
                [System.IO.File]::WriteAllText(
                    $badReproducedOrderPath,
                    ($badReproducedOrder | ConvertTo-Json),
                    [System.Text.UTF8Encoding]::new($false))
                $badReproducedStdoutPath = Join-Path $tempDirectory 'bad-reproduced.stdout.log'
                [System.IO.File]::WriteAllText(
                    $badReproducedStdoutPath,
                    'paged-queued',
                    [System.Text.UTF8Encoding]::new($false))
                $badReproducedPath = Join-Path $tempDirectory 'bad-reproduced.json'
                $badReproduced = [ordered]@{
                    Status = 'Failed';
                    ExecutionStatus = 'Failed';
                    ReproductionStatus = 'Reproduced';
                    Mode = 'test-contamination';
                    Iteration = 1;
                    Seed = 7;
                    Filter = 'self-test';
                    ControllerPid = 123;
                    StartUtc = '2026-08-23T00:00:00Z';
                    EndUtc = '2026-08-23T00:00:01Z';
                    ExitCode = 0;
                    ControllerTimedOut = $true;
                    TimedOut = $true;
                    TrxPath = (Join-Path $tempDirectory 'missing.trx');
                    StdoutPath = $badReproducedStdoutPath;
                    StderrPath = (Join-Path $tempDirectory 'stderr.log');
                    TestOrderPath = $badReproducedOrderPath;
                    TrxStatus = 'Missing';
                    TotalCount = 0;
                    CleanupTimedOut = $false;
                    CleanupError = $null;
                }
                [System.IO.File]::WriteAllText(
                    $badReproducedPath,
                    ($badReproduced | ConvertTo-Json),
                    [System.Text.UTF8Encoding]::new($false))
                $badReproducedResult = Read-SmartGuardWpfStabilityProbeResult -Path $badReproducedPath
                $badReproducedResult.Error | Should -Match 'Reproduced'

                $validReproducedTrxPath = Join-Path $tempDirectory 'valid-reproduced.trx'
                [System.IO.File]::WriteAllText(
                    $validReproducedTrxPath,
                    '<TestRun><ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="0" failed="1" error="0" timeout="0" aborted="0" inconclusive="0" notExecuted="0" /></ResultSummary><Results><UnitTestResult testName="self-test" outcome="Failed" /></Results></TestRun>',
                    [System.Text.UTF8Encoding]::new($false))
                $validReproducedOrderPath = Join-Path $tempDirectory 'valid-reproduced-order.json'
                $validReproducedOrder = [ordered]@{
                    Status = 'Captured';
                    Mode = 'test-contamination';
                    Iteration = 1;
                    Seed = 7;
                    Filter = 'self-test';
                    Tests = @([ordered]@{ Ordinal = 1; Name = 'self-test' });
                }
                [System.IO.File]::WriteAllText(
                    $validReproducedOrderPath,
                    ($validReproducedOrder | ConvertTo-Json),
                    [System.Text.UTF8Encoding]::new($false))
                $validReproducedPath = Join-Path $tempDirectory 'valid-reproduced.json'
                $validReproduced = [ordered]@{
                    Status = 'Failed';
                    ExecutionStatus = 'Failed';
                    ReproductionStatus = 'Reproduced';
                    Mode = 'test-contamination';
                    Iteration = 1;
                    Seed = 7;
                    Filter = 'self-test';
                    ControllerPid = 123;
                    StartUtc = '2026-08-23T00:00:00Z';
                    EndUtc = '2026-08-23T00:00:01Z';
                    ExitCode = 1;
                    ControllerTimedOut = $false;
                    TimedOut = $false;
                    TrxPath = $validReproducedTrxPath;
                    StdoutPath = $badReproducedStdoutPath;
                    StderrPath = (Join-Path $tempDirectory 'stderr.log');
                    TestOrderPath = $validReproducedOrderPath;
                    TrxStatus = 'Failed';
                    TotalCount = 1;
                    CleanupTimedOut = $false;
                    CleanupError = $null;
                }
                [System.IO.File]::WriteAllText(
                    $validReproducedPath,
                    ($validReproduced | ConvertTo-Json),
                    [System.Text.UTF8Encoding]::new($false))
                $validReproducedResult = Read-SmartGuardWpfStabilityProbeResult -Path $validReproducedPath
                $validReproducedResult.Status | Should -Be 'Failed'
                $validReproducedResult.ReproductionStatus | Should -Be 'Reproduced'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'WPF stability probe reproduction gate distinguishes expected failure from infrastructure failure' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')

            $notReproduced = [pscustomobject]@{
                ControllerExitCode = 0
                ControllerTimedOut = $false
                ControllerEvidenceError = ''
                CaptureStatus = 'Passed'
                Result = [pscustomobject]@{
                    Status = 'Passed'
                    ReproductionStatus = 'NotReproduced'
                    ControllerTimedOut = $false
                    TimedOut = $false
                    ExitCode = 0
                    TrxStatus = 'Passed'
                    CleanupTimedOut = $false
                    CleanupError = $null
                }
            }
            Get-SmartGuardWpfStabilityProbeExitCode -Results @($notReproduced) -RequireReproduction | Should -Be 2

            $reproduced = [pscustomobject]@{
                ControllerExitCode = 0
                ControllerTimedOut = $false
                ControllerEvidenceError = ''
                CaptureStatus = 'Passed'
                Result = [pscustomobject]@{
                    Status = 'Failed'
                    ReproductionStatus = 'Reproduced'
                    ControllerTimedOut = $false
                    TimedOut = $false
                    ExitCode = 23
                    TrxStatus = 'Failed'
                    CleanupTimedOut = $false
                    CleanupError = $null
                }
            }
            Get-SmartGuardWpfStabilityProbeExitCode -Results @($reproduced) -RequireReproduction | Should -Be 0

            $reproducedWithoutCapture = [pscustomobject]@{
                ControllerExitCode = 0
                ControllerTimedOut = $false
                ControllerEvidenceError = ''
                CaptureStatus = 'Failed'
                Result = [pscustomobject]@{
                    Status = 'Failed'
                    ReproductionStatus = 'Reproduced'
                    ControllerTimedOut = $false
                    CleanupTimedOut = $false
                    CleanupError = $null
                }
            }
            Get-SmartGuardWpfStabilityProbeExitCode -Results @($reproducedWithoutCapture) -RequireReproduction | Should -Be 1

            $invalidReproduced = [pscustomobject]@{
                ControllerExitCode = 0
                ControllerTimedOut = $false
                ControllerEvidenceError = ''
                CaptureStatus = 'Passed'
                Result = [pscustomobject]@{
                    Status = 'Failed'
                    ReproductionStatus = 'Reproduced'
                    ControllerTimedOut = $true
                    TimedOut = $true
                    ExitCode = 0
                    TrxStatus = 'Missing'
                    CleanupTimedOut = $false
                    CleanupError = $null
                }
            }
            Get-SmartGuardWpfStabilityProbeExitCode -Results @($invalidReproduced) -RequireReproduction | Should -Be 1

            $maskedControllerTimeout = [pscustomobject]@{
                ControllerExitCode = 0
                ControllerTimedOut = $true
                ControllerEvidenceError = ''
                CaptureStatus = 'Passed'
                Result = [pscustomobject]@{
                    Status = 'Passed'
                    ReproductionStatus = 'NotReproduced'
                    ControllerTimedOut = $false
                    TimedOut = $false
                    ExitCode = 0
                    TrxStatus = 'Passed'
                    CleanupTimedOut = $false
                    CleanupError = $null
                }
            }
            Get-SmartGuardWpfStabilityProbeExitCode -Results @($maskedControllerTimeout) | Should -Be 1
            Get-SmartGuardWpfStabilityProbeExitCode -Results @($maskedControllerTimeout) -RequireReproduction | Should -Be 1
        }

        It 'WPF stability probe controller records real non-zero and timeout children' {
            $root = Split-Path -Parent $PSScriptRoot
            $projectPath = Join-Path $root 'Tests\SmartGuard.Settings.StabilityProbe\SmartGuard.Settings.StabilityProbe.csproj'
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-wpf-probe-child-' + [guid]::NewGuid().ToString('N'))
            $failedDirectory = Join-Path $tempDirectory 'failed'
            $timeoutDirectory = Join-Path $tempDirectory 'timeout'
            $selectionPath = Join-Path $tempDirectory 'selection.txt'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                [System.IO.File]::WriteAllLines(
                    $selectionPath,
                    @('SmartGuard.Settings.Tests.WpfStaTestHostTests.self_test'),
                    [System.Text.UTF8Encoding]::new($false))
                # The probe is not a test project, so nothing else restores it (a clean CI checkout has no
                # assets file); restore it here before the --no-restore runs below.
                & dotnet restore $projectPath | Out-Null
                $LASTEXITCODE | Should -Be 0
                & dotnet run --project $projectPath --no-restore -- `
                    --mode test-contamination `
                    --run-directory $failedDirectory `
                    --iteration 1 `
                    --seed 7 `
                    --filter 'self-test' `
                    --timeout-seconds 5 `
                    --selection-path $selectionPath `
                    --test-command 'exit /b 23'
                $LASTEXITCODE | Should -Be 0
                $failed = Get-Content -LiteralPath (Join-Path $failedDirectory 'probe-result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $failed.Status | Should -Be 'Failed'
                $failed.ExitCode | Should -Be 23
                $failed.TimedOut | Should -BeFalse
                $failedOrder = Get-Content -LiteralPath (Join-Path $failedDirectory 'test-order.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $failedOrder.Status | Should -Be 'Selection'
                @($failedOrder.Tests).Count | Should -Be 1

                & dotnet run --project $projectPath --no-restore -- `
                    --mode test-contamination `
                    --run-directory $timeoutDirectory `
                    --iteration 2 `
                    --seed 8 `
                    --filter 'self-test' `
                    --timeout-seconds 1 `
                    --selection-path $selectionPath `
                    --test-command 'ping 127.0.0.1 -n 30'
                $LASTEXITCODE | Should -Be 0
                $timeout = Get-Content -LiteralPath (Join-Path $timeoutDirectory 'probe-result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $timeout.Status | Should -Be 'Failed'
                $timeout.TimedOut | Should -BeTrue
                $timeout.TestPid | Should -BeGreaterThan 0
                $timeoutOrder = Get-Content -LiteralPath (Join-Path $timeoutDirectory 'test-order.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $timeoutOrder.Status | Should -Be 'Selection'
                @($timeoutOrder.Tests).Count | Should -Be 1
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'adversarial review document defines required WPF stability triggers and fields' {
            $root = Split-Path -Parent $PSScriptRoot
            $reviewPath = Join-Path $root 'docs\testing\WPF-TEST-ADVERSARIAL-REVIEW.md'
            Test-Path -LiteralPath $reviewPath | Should -Be $true
            $content = Get-Content -LiteralPath $reviewPath -Raw -Encoding UTF8

            $requiredTriggers = @(
                'WpfApplicationFixture',
                'WpfStaTestHost',
                'test-project orchestration',
                'global static hooks',
                'timeout/retry',
                'parallelism',
                'ThreadPool',
                'background-flight lifetime',
                'structured result format'
            )
            $triggerMatches = @($requiredTriggers | Where-Object { $content.Contains($_) })
            $triggerMatches.Count | Should -Be $requiredTriggers.Count
            $triggerMatches.Count | Should -BeGreaterThan 0

            foreach ($field in @(
                'claim',
                'falsification condition',
                'facts',
                'hypotheses',
                'unknowns',
                'counterexample',
                'cross-test contamination',
                'thread/queue/time',
                'cancellation/Dispose',
                'evidence completeness',
                'ADOPT',
                'REVISE',
                'BLOCK',
                'adopted changes',
                'commit',
                'RED/GREEN evidence path',
                'reviewer type'
            )) {
                $content | Should -Match ([regex]::Escape($field))
            }
        }
    }

    Describe 'Phase 7.5 dotnet publish chain' {
        It 'build.cmd delegates publish to SmartGuard.Cli' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'build.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.csproj'
            $content | Should -Match 'publish'
            $content | Should -Not -Match 'dotnet publish'
            $content | Should -Not -Match 'if exist "%~dp0bin\\SmartGuard\.Cli\.exe"'
            $content | Should -Match 'dotnet run --project'
            $content | Should -Match '--root "%~dp0\."'
        }

        It 'DesktopDistributionManifest owns the desktop publish and payload lists' {
            $root = Split-Path -Parent $PSScriptRoot
            $manifest = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Distribution\DesktopDistributionManifest.cs') -Raw -Encoding UTF8
            $publisher = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Build\DesktopSuitePublisher.cs') -Raw -Encoding UTF8
            $copier = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Staging\PayloadCopier.cs') -Raw -Encoding UTF8
            $layout = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Staging\StagingLayout.cs') -Raw -Encoding UTF8
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $slnx = Get-Content -LiteralPath (Join-Path $root 'SmartGuard.slnx') -Raw -Encoding UTF8
            $settingsProject = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Settings\SmartGuard.Settings.csproj') -Raw -Encoding UTF8
            $settingsTests = Get-Content -LiteralPath (Join-Path $root 'Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj') -Raw -Encoding UTF8
            $manifest | Should -Match 'PublishedProjects'
            $manifest | Should -Match 'PayloadEntries'
            $manifest | Should -Match 'InstallerProcesses'
            $manifest | Should -Not -Match 'SmartGuard\.LogViewer\.csproj'
            $manifest | Should -Not -Match 'LogViewerExeFileName'
            $publisher | Should -Match 'DesktopDistributionManifest\.PublishedProjects'
            $copier | Should -Match 'DesktopDistributionManifest\.PayloadEntries'
            $layout | Should -Match 'DesktopDistributionManifest\.RequiredStagingPaths'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Files\.iss\.inc'
            $iss | Should -Match 'Exec\(EngineExe'
            $iss | Should -Match '--uninstall'
            $iss | Should -Not -Match 'SmartGuard\.LogViewer\.exe'
            $slnx | Should -Not -Match 'src/SmartGuard\.LogViewer/SmartGuard\.LogViewer\.csproj'
            $settingsProject | Should -Not -Match 'SmartGuard\.LogViewer\.csproj'
            $settingsTests | Should -Not -Match 'SmartGuard\.LogViewer\.csproj'
            $publisher | Should -Match '--self-contained false'
            $publisher | Should -Match 'win-x64'
        }

        It 'Directory.Build.props pins framework-dependent publish defaults' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw -Encoding UTF8
            $content | Should -Match '<SelfContained>false</SelfContained>'
        }

        It 'Setup-All.cmd is removed in favor of documented SmartGuard.Cli workflow' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Setup-All.cmd') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'scripts\Publish-All.ps1') | Should -Be $false
        }

        It 'runtime error messages must not reference removed Publish-All.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            @(
                'Tests\Integration\SmartGuardStop.ps1'
                'Tests\Integration\TrayCoreUserFlow.Helpers.ps1'
                'Tests\Integration\InstallerUserFlow.Helpers.ps1'
                'Start-Core.cmd'
                'Debug-Engine.cmd'
                'Register-AllTasks.cmd'
            ) | ForEach-Object {
                $content = Get-Content -LiteralPath (Join-Path $root $_) -Raw -Encoding UTF8
                $content | Should -Not -Match 'Publish-All\.ps1' -Because $_
            }
        }

        It 'legacy per-project Publish scripts are removed' {
            $root = Split-Path -Parent $PSScriptRoot
            @(
                'scripts\Publish-Engine.ps1'
                'scripts\Publish-Tray.ps1'
                'scripts\Publish-LogViewer.ps1'
                'scripts\Publish-Settings.ps1'
            ) | ForEach-Object {
                Test-Path -LiteralPath (Join-Path $root $_) | Should -Be $false -Because $_
            }
        }
    }

    Describe 'Phase 7.6 documentation sync' {
        It 'README documents build.cmd and canonical architecture docs' {
            $root = Split-Path -Parent $PSScriptRoot
            $readme = Get-Content -LiteralPath (Join-Path $root 'README.md') -Raw -Encoding UTF8
            $readme | Should -Match 'build\.cmd'
            $readme | Should -Match 'ARCHITECTURE-CONTRACT'
            $readme | Should -Not -Match 'Publish-Engine\.ps1'
            $readme | Should -Match 'docs/archive/README\.md'
        }
    }

    Describe 'Engine packaging' {
        It 'builds core engine without console window' {
            $root = Split-Path -Parent $PSScriptRoot
            $csproj = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Engine\SmartGuard.Engine.csproj') -Raw
            $csproj | Should -Match '<OutputType>WinExe</OutputType>'
        }
    }

    Describe 'Phase 3.1 install CLI' {
        It 'Program routes install and uninstall commands' {
            $root = Split-Path -Parent $PSScriptRoot
            $program = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Engine\Program.cs') -Raw -Encoding UTF8
            $program | Should -Match 'EngineCommandMode\.Install'
            $program | Should -Match 'EngineCommandMode\.Uninstall'
            $program | Should -Match 'InstallCommands\.RunInstall'
            $program | Should -Match 'InstallCommands\.RunUninstall'
        }
    }

    Describe 'Phase 6.1 scheduled task registrar' {
        It 'InstallCommands registers tasks via ScheduledTaskRegistrar not PowerShell scripts' {
            $root = Split-Path -Parent $PSScriptRoot
            $install = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Cli\InstallCommands.cs') -Raw -Encoding UTF8
            $install | Should -Match 'ScheduledTaskRegistrar\.RegisterGuardian'
            $install | Should -Match 'ScheduledTaskRegistrar\.RegisterTray'
            $install | Should -Not -Match 'RunPowerShellScript'
            $install | Should -Not -Match 'Register-SmartGuardTask\.ps1'
        }

        It 'AutoStartService registers missing tasks via ScheduledTaskRegistrar' {
            $root = Split-Path -Parent $PSScriptRoot
            $autoStart = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\AutoStartService.cs') -Raw -Encoding UTF8
            $autoStart | Should -Match 'ScheduledTaskRegistrar\.RegisterIfMissing'
            $autoStart | Should -Not -Match 'Register-SmartGuardTask\.ps1'
            $autoStart | Should -Not -Match 'Register-TrayTask\.ps1'
        }
    }

    Describe 'Phase 6.2 exe-only launchers' {
        It 'Start-Core.cmd delegates to SmartGuard.Cli start guardian' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Start-Core.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'start guardian'
            $content | Should -Not -Match 'powershell\.exe'
            $content | Should -Not -Match 'SmartGuard\.Core\.ps1'
        }

        It 'Debug-Engine.cmd delegates to SmartGuard.Cli start engine' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Debug-Engine.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'start engine'
            $content | Should -Not -Match 'powershell\.exe'
        }

        It 'Start-Tray.cmd delegates to SmartGuard.Cli start tray' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Start-Tray.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'start tray'
            $content | Should -Not -Match 'powershell\.exe'
            $content | Should -Not -Match 'SmartGuard\.Tray\.ps1'
        }

        It 'Restart-Tray.cmd delegates to SmartGuard.Cli restart tray' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Restart-Tray.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'restart tray'
            $content | Should -Not -Match 'powershell\.exe'
        }

        It 'DesktopShellRegistration does not fall back to powershell.exe' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Shell\DesktopShellRegistration.cs') -Raw -Encoding UTF8
            $content | Should -Not -Match 'powershell\.exe'
        }

    }

    Describe 'Phase 7.1 dev launchers cmd-only' {
        It 'does not ship PS dev launcher scripts at repo root' {
            $root = Split-Path -Parent $PSScriptRoot
            @(
                'Start-Core.ps1'
                'Register-AllTasks.ps1'
                'Restart-Tray.ps1'
                'Start-SmartGuard.ps1'
            ) | ForEach-Object {
                Test-Path -LiteralPath (Join-Path $root $_) | Should -Be $false -Because $_
            }
        }

        It 'Register-AllTasks.cmd registers via SmartGuard.Cli install' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Register-AllTasks.cmd') -Raw -Encoding UTF8
            $content | Should -Match 'SmartGuard\.Cli\.exe'
            $content | Should -Match 'install'
            $content | Should -Not -Match 'powershell\.exe'
            $content | Should -Not -Match 'Register-SmartGuardTask\.ps1'
        }

        It 'Start-SmartGuard.cmd is removed in favor of SmartGuard.Cli' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Start-SmartGuard.cmd') | Should -Be $false
        }
    }

    Describe 'Phase 7.2 status via SmartGuard.Cli' {
        It 'Status.cmd is removed and StatusReporter is the status true source' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Status.cmd') | Should -Be $false
            $reporter = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\StatusReporter.cs') -Raw -Encoding UTF8
            $reporter | Should -Match 'ScheduledTaskRegistrar\.TaskNames'
            $reporter | Should -Match 'StartupLogFile'
            $cli = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Cli\CliRouter.cs') -Raw -Encoding UTF8
            $cli | Should -Match 'StatusReporter\.FormatStatus'
        }
    }

    Describe 'Phase 7.3 legacy dev scripts' {
        It 'does not ship Repair-Encoding helpers that recreate PS stack' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Repair-Encoding.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'Repair.cmd') | Should -Be $false
        }

        It 'Setup-All.cmd is not shipped at repo root' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'Setup-All.cmd') | Should -Be $false
        }

        It 'archives one-shot Migrate-RenameToSmartGuard script' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'scripts\Migrate-RenameToSmartGuard.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'scripts\archive\Migrate-RenameToSmartGuard.ps1') | Should -Be $true
        }

        It 'Run-Tests.cmd uses repo-relative path' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.cmd') -Raw -Encoding UTF8
            $content | Should -Match '%~dp0'
            $content | Should -Not -Match 'D:\\Project\\SmartGuard'
        }
    }

    Describe 'Workspace cleanup script' {
        It 'Clean-Workspace.ps1 supports DryRun without deleting build output' {
            $root = Split-Path -Parent $PSScriptRoot
            $script = Join-Path $root 'scripts\Clean-Workspace.ps1'
            Test-Path -LiteralPath $script | Should -Be $true

            $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $script -DryRun -AllLocal 2>&1 | Out-String
            $output | Should -Match 'Size before:'
            $output | Should -Match 'Estimated reclaimable:'
            $output | Should -Match 'Worktrees under .worktrees/'
        }
    }

    Describe 'Phase 6.3 remove PS application stack' {
        It 'does not ship legacy PS application entry scripts' {
            $root = Split-Path -Parent $PSScriptRoot
            @(
                'lib\SmartGuard.Core.ps1'
                'lib\SmartGuard.Tray.ps1'
                'lib\SmartGuard.Settings.ps1'
                'lib\Show-LogViewer.ps1'
                'lib\SmartGuard.Functions.ps1'
                'Register-SmartGuardTask.ps1'
                'Register-TrayTask.ps1'
            ) | ForEach-Object {
                Test-Path -LiteralPath (Join-Path $root $_) | Should -Be $false -Because $_
            }
        }

        It 'ScheduledTaskRegistrar requires published exe files' {
            $root = Split-Path -Parent $PSScriptRoot
            $content = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\ScheduledTaskRegistrar.cs') -Raw -Encoding UTF8
            $content | Should -Match 'FileNotFoundException'
            $content | Should -Not -Match 'SmartGuard\.Core\.ps1'
            $content | Should -Not -Match 'SmartGuard\.Tray\.ps1'
            $content | Should -Not -Match 'powershell\.exe'
        }

        It 'ExternalToolLauncher opens only published desktop exes' {
            $root = Split-Path -Parent $PSScriptRoot
            $tray = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Tray\Infrastructure.cs') -Raw -Encoding UTF8
            $tray | Should -Match 'SettingsMainPageLauncher\.Open'
            $tray | Should -Match 'OpenLogsPage'
            $tray | Should -Match 'SettingsLogsPageLauncher\.Open'
            $tray | Should -Not -Match 'OpenLogViewer'
            $tray | Should -Not -Match 'SmartGuard\.Settings\.ps1'
            $tray | Should -Not -Match 'Show-LogViewer\.ps1'
            $tray | Should -Not -Match 'powershell\.exe'
            $mainLauncher = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\SettingsMainPageLauncher.cs') -Raw -Encoding UTF8
            $mainLauncher | Should -Match 'SmartGuardPaths\.SettingsExe'
            $logsLauncher = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\SettingsLogsPageLauncher.cs') -Raw -Encoding UTF8
            $logsLauncher | Should -Match 'SmartGuardPaths\.SettingsExe'
        }
    }

    Describe 'Phase 5 Inno installer' {
        It 'upgrade path detects existing installation via registry' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'GetExistingSmartGuardInstallPath'
            $iss | Should -Match 'UsePreviousAppDir=yes'
            $iss | Should -Match 'HKCU'
            $iss | Should -Match 'HKLM'
            $iss | Should -Match 'RegQueryStringValue'
        }

        It 'SmartGuard.iss includes signed publisher and install hooks' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $iss | Should -Match '#define MyAppPublisher "rainiva"'
            $iss | Should -Match 'https://github.com/rainiva/SmartGuard'
            $iss | Should -Match 'SetupIconFile='
            $iss | Should -Match 'ShouldDeleteUserConfig'
            $iss | Should -Match 'ShouldDeleteUserLogs'
            $iss | Should -Match 'license_zh-CN\.txt'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $writer | Should -Match '--skip-publish'
            $writer | Should -Match '\{#EngineExeName\}'
            $iss | Should -Not -Match 'Register-SmartGuardTask\.ps1'
            $iss | Should -Not -Match 'Register-TrayTask\.ps1'
        }

        It 'stops SmartGuard processes before install and uninstall' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $manifest = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Distribution\DesktopDistributionManifest.cs') -Raw -Encoding UTF8
            $iss | Should -Match 'CloseApplications=no'
            $iss | Should -Match 'RestartIfNeededByRun=no'
            $iss | Should -Match 'EnsureSmartGuardStopped'
            $iss | Should -Match '\[InstallDelete\]'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.LegacyCleanup\.iss\.inc'
            $iss | Should -Match 'function PrepareToInstall'
            $iss | Should -Match 'function TryStopSmartGuardProcesses'
            $iss | Should -Match 'CurPageID = wpReady'
            $iss | Should -Match 'Exec\(EngineExe'
            $iss | Should -Match '\(ResultCode = 0\)'
            $iss | Should -Not -Match 'tasklist'
            $iss | Should -Not -Match "Pos\('SmartGuard\.Tray\.exe'"
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $writer | Should -Not -Match 'restartreplace'
            $manifest | Should -Match 'SmartGuardPaths\.EngineExeFileName'
            $manifest | Should -Match 'SmartGuardPaths\.TrayExeFileName'
            $manifest | Should -Match 'SmartGuardPaths\.SettingsExeFileName'
            $manifest | Should -Not -Match 'SmartGuard\.LogViewer\.csproj'
            $manifest | Should -Not -Match 'LogViewerExeFileName'
            $iss | Should -Not -Match 'Get-Process -Name SmartGuard\*'
            $iss | Should -Not -Match 'Get-CimInstance Win32_Process'
            $iss | Should -Not -Match 'CurStepChanged'
            $iss | Should -Match 'CurUninstallStepChanged'
            $iss | Should -Match 'SolidCompression=no'
            $stopBlock = [regex]::Match($iss, '(?s)function TryStopSmartGuardProcesses.*?end;').Value
            $stopBlock | Should -Not -BeNullOrEmpty
            $stopBlock | Should -Not -Match 'schtasks /End'
            $stopBlock | Should -Not -Match 'taskkill\.exe'
        }

        It 'uninstall user-data choice uses explicit unchecked config and log checkboxes' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'function InitializeUninstall\(\): Boolean'
            $iss | Should -Match 'procedure InitializeUninstallProgressForm'
            $iss | Should -Match 'UninstallSilent'
            $iss | Should -Match 'DeleteUserConfigCheckBox := TNewCheckBox\.Create'
            $iss | Should -Match 'DeleteUserLogsCheckBox := TNewCheckBox\.Create'
            $iss | Should -Match '\u5220\u9664\u7528\u6237\u6570\u636E\uFF08\u914D\u7F6E\u4E0E\u72B6\u6001\uFF09'
            $iss | Should -Match '\u5220\u9664\u65E5\u5FD7\u6587\u4EF6'
            $iss | Should -Match 'DeleteUserConfigCheckBox\.Checked := False'
            $iss | Should -Match 'DeleteUserLogsCheckBox\.Checked := False'
            $iss | Should -Not -Match 'CreateCustomPage'
            $iss | Should -Not -Match 'CreateCustomForm'
            $iss | Should -Not -Match 'Parent := WizardForm'
        }

        It 'starts guardian after install before tray' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $writer | Should -Match '\{#GuardianTaskName\}'
            $writer | Should -Match '\u6B63\u5728\u542F\u52A8\u6838\u5FC3\u670D\u52A1'
        }

        It 'allows only one installed SmartGuard instance' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'UsePreviousAppDir=yes'
            $iss | Should -Match 'GetExistingSmartGuardInstallPath'
            $iss | Should -Match 'wpSelectDir'
        }

        It 'upgrade path re-registers tasks after install' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $writer | Should -Match '--install.*--skip-publish'
            $writer | Should -Match 'schtasks\.exe.*Run.*\{#GuardianTaskName\}'
        }

        It 'installer staging and version logic are in C# packaging project' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'installer\Build-Staging.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'installer\Build-Installer.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'installer\InstallVersion.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'installer\InstallStaging.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\SmartGuard.Packaging.csproj') | Should -Be $true
        }
    }

    Describe 'Phase 8 architecture single source of truth' {
        It 'live tree no longer keeps the retired icon draft generator chain' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'scripts\generate-smartguard-icons.py') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'assets\icon') | Should -Be $false
        }

        It 'historical task-contract and diagnosis docs live under docs/archive instead of top-level docs' {
            $root = Split-Path -Parent $PSScriptRoot
            $archiveRoot = Join-Path $root 'docs\archive'
            $historical = @(Get-ChildItem -LiteralPath $archiveRoot -File -Filter '*.md' | Where-Object {
                $_.BaseName -ne 'README'
            })
            $historical.Count | Should -BeGreaterThan 0

            foreach ($doc in $historical) {
                Test-Path -LiteralPath (Join-Path $root ('docs\' + $doc.Name)) | Should -Be $false
                Test-Path -LiteralPath $doc.FullName | Should -Be $true
            }
        }

        It 'archive README declares that canonical docs override historical snapshots' {
            $root = Split-Path -Parent $PSScriptRoot
            $readme = Get-Content -LiteralPath (Join-Path $root 'docs\archive\README.md') -Raw -Encoding UTF8
            $readme | Should -Match 'historical'
            $readme | Should -Match 'ARCHITECTURE-CONTRACT'
            $readme | Should -Match 'MULTI-SOURCE-GOVERNANCE-ACCEPTANCE'
            $readme | Should -Match 'single-entry-single-source'
        }

        It 'ARCHITECTURE-CONTRACT.md exists with governance sections' {
            $root = Split-Path -Parent $PSScriptRoot
            $contract = Join-Path $root 'docs\ARCHITECTURE-CONTRACT.md'
            Test-Path -LiteralPath $contract | Should -Be $true
            $content = Get-Content -LiteralPath $contract -Raw -Encoding UTF8
            $content | Should -Match '\u771F\u6E90\u6CE8\u518C\u8868'
            $content | Should -Match 'InstallRootResolver'
            $content | Should -Match 'GuardConfigRepository'
        }

        It 'power lifecycle acceptance script parses in Windows PowerShell 5.1' {
            $root = Split-Path -Parent $PSScriptRoot
            $script = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-PowerLifecycleAcceptance.ps1') -Raw -Encoding UTF8
            $tokens = $null
            $errors = $null
            [System.Management.Automation.Language.Parser]::ParseInput(
                $script,
                [ref]$tokens,
                [ref]$errors) | Out-Null
            @($errors).Count | Should -Be 0
        }

        It 'single-entry single-source rule doc exists as the short governance entrypoint' {
            $root = Split-Path -Parent $PSScriptRoot
            $ruleDoc = Join-Path $root 'docs\architecture\single-entry-single-source.md'
            Test-Path -LiteralPath $ruleDoc | Should -Be $true
            $content = Get-Content -LiteralPath $ruleDoc -Raw -Encoding UTF8
            $content | Should -Match 'canonical owner'
            $content | Should -Match 'fallback'
            $content | Should -Match 'marker'
            $content | Should -Match 'DesktopShellRegistration'
            $content | Should -Match 'DesktopDistributionManifest'
            $content | Should -Match 'SmartGuard\.Settings\.exe --page logs'
        }

        It 'AGENTS.md routes architecture TDD and critical-module governance' {
            $root = Split-Path -Parent $PSScriptRoot
            $agents = Get-Content -LiteralPath (Join-Path $root 'AGENTS.md') -Raw -Encoding UTF8
            $projectRules = Get-Content -LiteralPath (Join-Path $root 'docs\PROJECT-RULES.md') -Raw -Encoding UTF8
            $agents | Should -Match '\u884C\u4E3A\u6539\u52A8\u5FC5\u987B TDD'
            $agents | Should -Match 'SmartGuard \u67B6\u6784\u786C\u95E8\u7981'
            $agents | Should -Match '\u5173\u952E\u6A21\u5757\u6CBB\u7406'
            $projectRules | Should -Match '### 2\.5 \u5173\u952E\u6A21\u5757\u6CBB\u7406'
            $projectRules | Should -Match '\u6A21\u5757\u4E0D\u53D8\u91CF'
        }

        It 'SmartGuard.Architecture.Tests project is wired in Run-Tests.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $dotnetTests = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Match 'Invoke-SmartGuardDotNetTests\.ps1'
            $dotnetTests | Should -Match 'SmartGuard\.Architecture\.Tests'
            Test-Path -LiteralPath (Join-Path $root 'Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj') | Should -Be $true
        }

        It 'installer integration helper builds via SmartGuard.Packaging not Build-Installer.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $helper = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\InstallerUserFlow.Helpers.ps1') -Raw -Encoding UTF8
            $helper | Should -Match 'SmartGuard\.Packaging'
            $helper | Should -Match 'installer'
            $helper | Should -Not -Match 'Build-Installer\.ps1'
        }

        It 'GitHub Actions workflow runs Run-Tests.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $workflow = Get-Content -LiteralPath (Join-Path $root '.github\workflows\test.yml') -Raw -Encoding UTF8
            $workflow | Should -Match 'Run-Tests\.ps1'
            $workflow | Should -Match 'windows-latest'
        }

        It 'installer delegates shell surfaces to InstallCommands instead of owning [Icons]' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $writer | Should -Match '\{#CliExeName\}'
            $writer | Should -Match '--create-desktop-shortcut'
            $iss | Should -Not -Match '(?ms)^\[Icons\]'
            $iss | Should -Not -Match 'DefaultGroupName='
            $iss | Should -Not -Match 'DisableProgramGroupPage='
        }

        It 'DesktopShellRegistration publishes one start menu shortcut not three legacy entries' {
            $root = Split-Path -Parent $PSScriptRoot
            $shellOwner = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Shell\DesktopShellRegistration.cs') -Raw -Encoding UTF8
            $liveStartMenu = if ($shellOwner -match '(?s)GetStartMenuShortcuts\(string root\)\s*\{(.*?)\n  \}') { $Matches[1] } else { '' }
            $liveStartMenu | Should -Match 'SmartGuard\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 托盘\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 设置\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 日志\.lnk'
            $liveStartMenu | Should -Not -Match '--page logs'
        }

        It 'solution and docs do not reference SmartGuard.LogViewer.Tests or three live shell entries' {
            $root = Split-Path -Parent $PSScriptRoot
            $slnx = Get-Content -LiteralPath (Join-Path $root 'SmartGuard.slnx') -Raw -Encoding UTF8
            $settingsProject = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Settings\SmartGuard.Settings.csproj') -Raw -Encoding UTF8
            $slnx | Should -Not -Match 'SmartGuard\.LogViewer\.Tests'
            $settingsProject | Should -Not -Match 'InternalsVisibleTo Include="SmartGuard\.LogViewer\.Tests"'

            $shellOwner = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\Shell\DesktopShellRegistration.cs') -Raw -Encoding UTF8
            $liveStartMenu = if ($shellOwner -match '(?s)GetStartMenuShortcuts\(string root\)\s*\{(.*?)\n  \}') { $Matches[1] } else { '' }
            $liveStartMenu | Should -Match 'SmartGuard\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 托盘\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 设置\.lnk'
            $liveStartMenu | Should -Not -Match 'SmartGuard 日志\.lnk'

            # Repo truth gates exempt only docs/archive/, scripts/archive/, and docs/superpowers/plans/.
            $docHits = @(rg -n 'SmartGuard\.LogViewer\.Tests|SmartGuard 日志\.lnk|SmartGuard 设置\.lnk|SmartGuard 托盘\.lnk|lib\\SmartGuard\.ico|lib\\SmartGuard\.Settings\.xaml|\[Icons\].*日志|SmartGuard\.LogViewer\.exe' (Join-Path $root 'docs') 2>$null | Where-Object {
                ($_ -notmatch 'docs[\\/]+archive[\\/]') `
                    -and ($_ -notmatch 'docs[\\/]+superpowers[\\/]+plans[\\/]') `
                    -and ($_ -notmatch 'docs[\\/]+ARCHITECTURE-CONTRACT\.md') `
                    -and ($_ -notmatch 'docs[\\/]+MULTI-SOURCE-GOVERNANCE-ACCEPTANCE\.md') `
                    -and ($_ -notmatch 'docs[\\/]+architecture[\\/]+single-entry-single-source\.md')
            })
            $docHits.Count | Should -Be 0 -Because ($docHits -join [Environment]::NewLine)

            $scriptHits = @(rg -n 'lib\\SmartGuard\.ico|lib\\SmartGuard\.Settings\.xaml' (Join-Path $root 'scripts') 2>$null | Where-Object {
                $_ -notmatch 'scripts[\\/]+archive[\\/]'
            })
            $scriptHits.Count | Should -Be 0 -Because ($scriptHits -join [Environment]::NewLine)
        }

        It 'repo truth gates exempt only archive and plan paths, never filename heuristics' {
            $root = Split-Path -Parent $PSScriptRoot
            $suite = Get-Content -LiteralPath (Join-Path $root 'Tests\SmartGuard.Tests.ps1') -Raw -Encoding UTF8
            $historyGate = if ($suite -match "(?s)It 'historical task-contract and diagnosis docs live under docs/archive instead of top-level docs' \{(.*?)\r?\n        \}\r?\n\r?\n        It 'archive README declares that canonical docs override historical snapshots'") { $Matches[1] } else { '' }
            $historyGate | Should -Match 'Get-ChildItem'
            $historyGate | Should -Not -Match "'[A-Za-z0-9][^']*\.md'"

            $docGate = if ($suite -match '(?s)# Repo truth gates exempt.*?\$docHits\.Count') { $Matches[0] } else { '' }
            $docGate | Should -Match 'docs/archive/'
            $docGate | Should -Match 'docs/superpowers/plans/'
            $docGate | Should -Not -Match '\d{4}-\d{2}-\d{2}'

            $scriptGate = if ($suite -match '(?s)\$scriptHits = @\(rg.*?\$scriptHits\.Count') { $Matches[0] } else { '' }
            $scriptGate | Should -Match 'scripts.*archive'
        }

        It 'installer does not package or advertise SmartGuard.LogViewer.exe as a live entry' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Not -Match 'SmartGuard\.LogViewer\.exe'
        }

        It 'installer does not keep live exe or task literals once generated includes exist' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Constants\.iss\.inc'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Run\.iss\.inc'
            $iss | Should -Not -Match '#define MyAppExeName "SmartGuard\.Tray\.exe"'
            $iss | Should -Not -Match 'UninstallDisplayIcon=\{app\}\\bin\\SmartGuard\.Tray\.exe'
            $iss | Should -Not -Match 'Filename: "\{app\}\\bin\\SmartGuard\.Engine\.exe"'
            $iss | Should -Not -Match 'Filename: "\{app\}\\bin\\SmartGuard\.Cli\.exe"'
            $iss | Should -Not -Match '/TN ""SmartGuard Guardian""'
        }

        It 'live scripts do not keep legacy SmartPowerPlan cleanup entrypoints' {
            $root = Split-Path -Parent $PSScriptRoot
            Test-Path -LiteralPath (Join-Path $root 'scripts\LegacyScheduledTaskNames.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'scripts\Unregister-LegacySmartPowerPlanTasks.ps1') | Should -Be $false
            Test-Path -LiteralPath (Join-Path $root 'scripts\archive\LegacyScheduledTaskNames.ps1') | Should -Be $true
            Test-Path -LiteralPath (Join-Path $root 'scripts\archive\Unregister-LegacySmartPowerPlanTasks.ps1') | Should -Be $true
        }

        It 'contract docs do not leave historical migration pointers in live guidance' {
            $root = Split-Path -Parent $PSScriptRoot
            $contract = Get-Content -LiteralPath (Join-Path $root 'docs\ARCHITECTURE-CONTRACT.md') -Raw -Encoding UTF8
            $acceptance = Get-Content -LiteralPath (Join-Path $root 'docs\MULTI-SOURCE-GOVERNANCE-ACCEPTANCE.md') -Raw -Encoding UTF8
            $contract | Should -Not -Match '\u89C1 MIGRATION'
            $contract | Should -Match 'generated/SmartGuard\.Distribution\.Constants\.iss\.inc'
            $contract | Should -Match 'LegacyScheduledTaskCatalog'
            $acceptance | Should -Match 'scripts/archive/'
            $acceptance | Should -Match 'SmartGuard\.Distribution\.Run\.iss\.inc'
        }

        It 'integration stop helpers delegate to Engine --uninstall' {
            $root = Split-Path -Parent $PSScriptRoot
            $shared = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\SmartGuardStop.ps1') -Raw -Encoding UTF8
            $tray = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Helpers.ps1') -Raw -Encoding UTF8
            $installer = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\InstallerUserFlow.Helpers.ps1') -Raw -Encoding UTF8
            $shared | Should -Match '--uninstall'
            $tray | Should -Match 'SmartGuardStop\.ps1'
            $installer | Should -Match 'SmartGuardStop\.ps1'
            $tray | Should -Not -Match 'schtasks /End'
            $installerStop = [regex]::Match(
                $installer,
                '(?s)function Stop-SmartGuardForInstallerTest.*?(?=\r?\nfunction |\z)').Value
            $installerStop | Should -Not -BeNullOrEmpty
            $installerStop | Should -Not -Match 'schtasks /End'
        }

        It 'StatusReporter and Inno use registrar task names' {
            $root = Split-Path -Parent $PSScriptRoot
            $status = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Configuration\StatusReporter.cs') -Raw -Encoding UTF8
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $writer = Get-Content -LiteralPath (Join-Path $root 'src\SmartGuard.Packaging\Inno\InnoDistributionManifestWriter.cs') -Raw -Encoding UTF8
            $status | Should -Match 'ScheduledTaskRegistrar'
            $iss | Should -Match 'generated\\SmartGuard\.Distribution\.Constants\.iss\.inc'
            $iss | Should -Not -Match 'SmartGuard Guardian'
            $writer | Should -Match 'ScheduledTaskRegistrar\.GuardianTaskName'
            $stopBlock = [regex]::Match(
                $iss,
                '(?s)function TryStopSmartGuardProcesses.*?(?=\r?\nfunction EnsureSmartGuardStopped)').Value
            $stopBlock | Should -Match '\\bin\\\{#EngineExeName\}'
            $stopBlock | Should -Not -Match 'SmartGuard\.Engine\.exe'
            $stopBlock | Should -Match '--uninstall'
        }

        It 'installer stop function consumes EngineExeName define instead of engine literal' {
            $root = Split-Path -Parent $PSScriptRoot
            $iss = Get-Content -LiteralPath (Join-Path $root 'installer\SmartGuard.iss') -Raw -Encoding UTF8
            $stop = [regex]::Match(
                $iss,
                '(?s)function TryStopSmartGuardProcesses.*?(?=\r?\nfunction EnsureSmartGuardStopped)').Value
            $stop | Should -Match '\\bin\\\{#EngineExeName\}'
            $stop | Should -Not -Match 'SmartGuard\.Engine\.exe'
        }

        It 'closeout docs describe implementation-path grep instead of fake full-repo silence' {
            $root = Split-Path -Parent $PSScriptRoot
            $contract = Get-Content -LiteralPath (Join-Path $root 'docs\ARCHITECTURE-CONTRACT.md') -Raw -Encoding UTF8
            $acceptance = Get-Content -LiteralPath (Join-Path $root 'docs\MULTI-SOURCE-GOVERNANCE-ACCEPTANCE.md') -Raw -Encoding UTF8
            $overview = Get-Content -LiteralPath (Join-Path $root 'docs\architecture\single-entry-single-source.md') -Raw -Encoding UTF8

            $contract | Should -Match 'implementation-path grep'
            $acceptance | Should -Match 'docs/tests may mention retired literals only as assertions or contract text'
            $overview | Should -Match 'Windows consumers are allowed only when they point back to canonical exe or embedded icon chain'
        }

        It 'implementation-path grep blocks live literals and fallback icons without scanning docs/tests' {
            $root = Split-Path -Parent $PSScriptRoot
            $hits = @(rg -n --pcre2 "RootPath \+ '\\bin\\SmartGuard\.Engine\.exe'|LoadWinFormsIcon\(SystemIcons\.Shield\)|SystemIcons\.Shield|SmartGuardPaths\.BrandIcon(?:\W|$)|lib\\SmartGuard\.ico" `
                (Join-Path $root 'installer') (Join-Path $root 'src') (Join-Path $root 'scripts') --glob '!**/scripts/archive/**' 2>$null)
            $hits.Count | Should -Be 0 -Because ($hits -join [Environment]::NewLine)
        }
    }

    Describe 'Run-Tests optimization contract' {
        It 'captured test process preserves the real non-zero child exit code' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-captured-command-' + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                $capturedCommand = Start-SmartGuardCapturedCommand `
                    -CommandText '"%SystemRoot%\System32\cmd.exe" /d /c exit 23' `
                    -ResultDirectory $tempDirectory `
                    -RunId 'exit-23'

                $capturedCommand.Process | Should -Not -BeNullOrEmpty
                $capturedCommand.StdoutPath | Should -Not -BeNullOrEmpty
                $capturedCommand.RunId | Should -Be 'exit-23'

                $result = Complete-SmartGuardCapturedCommand `
                    -Entry $capturedCommand `
                    -TimeoutSeconds 10

                $result.ExitCode | Should -Be 23
                $result.TimedOut | Should -BeFalse
                @(Get-ChildItem -LiteralPath $tempDirectory -Filter '*.exit').Count | Should -Be 0
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'captured command timeout stops its captured process tree promptly' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-captured-timeout-' + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                $capturedCommand = Start-SmartGuardCapturedCommand `
                    -CommandText 'ping 127.0.0.1 -n 30' `
                    -ResultDirectory $tempDirectory `
                    -RunId 'timeout'
                $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

                $result = Complete-SmartGuardCapturedCommand `
                    -Entry $capturedCommand `
                    -TimeoutSeconds 1

                $stopwatch.Stop()
                $result.TimedOut | Should -BeTrue
                $capturedCommand.Process.HasExited | Should -BeTrue
                $stopwatch.Elapsed | Should -BeLessThan ([TimeSpan]::FromSeconds(10))
            }
            finally {
                if ($null -ne $capturedCommand -and -not $capturedCommand.Process.HasExited) {
                    Stop-SmartGuardCapturedProcessTree -ProcessId $capturedCommand.Process.Id
                }
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'engine publish refreshes a stale binary older than the newest engine source' {
            # The published bin\SmartGuard.Engine.exe sat at an old date while
            # newer src commits shipped crash fixes; the integration layer then
            # kept exercising the stale engine because Ensure only checked
            # existence, never freshness.
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-stale-publish-' + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path (Join-Path $tempDirectory 'bin'))
            [void](New-Item -ItemType Directory -Path (Join-Path $tempDirectory 'src\SmartGuard.Engine'))
            try {
                $enginePath = Join-Path $tempDirectory 'bin\SmartGuard.Engine.exe'
                $sourcePath = Join-Path $tempDirectory 'src\SmartGuard.Engine\Program.cs'
                $markerPath = Join-Path $tempDirectory 'build-invoked.txt'
                $buildCmdPath = Join-Path $tempDirectory 'build.cmd'
                [System.IO.File]::WriteAllText($enginePath, 'stale engine', [System.Text.UTF8Encoding]::new($false))
                [System.IO.File]::WriteAllText($sourcePath, '// fresh source', [System.Text.UTF8Encoding]::new($false))
                [System.IO.File]::WriteAllText(
                    $buildCmdPath,
                    "@echo off`r`necho invoked> `"$markerPath`"`r`nexit /b 0`r`n",
                    [System.Text.UTF8Encoding]::new($false))
                (Get-Item -LiteralPath $enginePath).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-2)
                (Get-Item -LiteralPath $sourcePath).LastWriteTimeUtc = [DateTime]::UtcNow.AddMinutes(-5)

                Ensure-SmartGuardEnginePublished -RepoRoot $tempDirectory

                Test-Path -LiteralPath $markerPath |
                    Should -BeTrue -Because 'a stale published engine must trigger a republish'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'engine publish output never contaminates the structured suite result stream' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-suite-output-' + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                $buildCmdPath = Join-Path $tempDirectory 'build.cmd'
                $buildCmd = "@echo off`r`necho BUILD-SUCCESS-OUTPUT`r`nexit /b 0`r`n"
                [System.IO.File]::WriteAllText(
                    $buildCmdPath,
                    $buildCmd,
                    [System.Text.UTF8Encoding]::new($false))

                Mock Ensure-SmartGuardDotNetTestBuild {}
                Mock Invoke-SmartGuardDotNetTestProcesses {
                    param($ProjectPaths)
                    return [pscustomobject]@{
                        Project = $ProjectPaths[0]
                        RunId = 'mock'
                        TrxPath = 'mock.trx'
                        ExitCode = 0
                        Output = ''
                        TimedOut = $false
                        Result = 'Passed'
                        PassedCount = 1
                        FailedCount = 0
                        SkippedCount = 0
                        TotalCount = 1
                        Error = ''
                    }
                }

                $suiteOutput = @(
                    Invoke-SmartGuardDotNetTestSuite `
                        -RepoRoot $tempDirectory `
                        -Tier fast
                )

                $suiteOutput.Count | Should -Be 1
                $suiteOutput[0].GetType().FullName |
                    Should -Be 'System.Management.Automation.PSCustomObject'
                $suiteOutput[0].Result | Should -Be 'Passed'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'parallel dotnet process entries share one batch deadline' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-shared-deadline-' + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                $script:completedEntries = @()
                $script:nextFakeProcessId = 1000
                Mock Start-SmartGuardCapturedCommand {
                    param($ResultDirectory, $RunId)
                    $script:nextFakeProcessId++
                    return [pscustomobject]@{
                        Process = [pscustomobject]@{ Id = $script:nextFakeProcessId }
                        StdoutPath = Join-Path $ResultDirectory "$RunId.stdout.log"
                        StderrPath = Join-Path $ResultDirectory "$RunId.stderr.log"
                        RunId = $RunId
                    }
                }
                Mock Complete-SmartGuardCapturedCommand {
                    param($Entry)
                    $script:completedEntries += $Entry
                    return [pscustomobject]@{
                        ExitCode = 0
                        Output = ''
                        TimedOut = $false
                        RunId = $Entry.RunId
                    }
                }
                Mock Read-SmartGuardTrxResult {
                    return [pscustomobject]@{
                        Result = 'Passed'
                        PassedCount = 1
                        FailedCount = 0
                        SkippedCount = 0
                        TotalCount = 1
                        Error = ''
                    }
                }

                $projectPaths = @(
                    (Join-Path $tempDirectory 'First.Tests.csproj'),
                    (Join-Path $tempDirectory 'Second.Tests.csproj')
                )
                $results = @(
                    Invoke-SmartGuardDotNetTestProcesses `
                        -ProjectPaths $projectPaths `
                        -ResultDirectory $tempDirectory `
                        -RunId 'shared-deadline' `
                        -TimeoutSeconds 600
                )

                $results.Count | Should -Be 2
                $script:completedEntries.Count | Should -Be 2
                foreach ($entry in $script:completedEntries) {
                    $entry.PSObject.Properties.Name | Should -Contain 'DeadlineUtc'
                    $entry.DeadlineUtc | Should -BeOfType ([DateTime])
                }
                @(
                    $script:completedEntries |
                        ForEach-Object { $_.DeadlineUtc.Ticks } |
                        Select-Object -Unique
                ).Count | Should -Be 1
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'captured completion honors an entry deadline instead of granting a new timeout' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-entry-deadline-' + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                $script:deadlineFakeProcess = [pscustomobject]@{
                    HasExited = $false
                    Id = 4242
                    ExitCode = 1
                }
                $script:deadlineFakeProcess |
                    Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {}
                $entry = [pscustomobject]@{
                    Process = $script:deadlineFakeProcess
                    StdoutPath = Join-Path $tempDirectory 'missing.stdout.log'
                    StderrPath = Join-Path $tempDirectory 'missing.stderr.log'
                    RunId = 'expired-deadline'
                    DeadlineUtc = [DateTime]::UtcNow.AddSeconds(-1)
                }
                Mock Stop-SmartGuardCapturedProcessTree {
                    $script:deadlineFakeProcess.HasExited = $true
                }
                $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

                $result = Complete-SmartGuardCapturedCommand `
                    -Entry $entry `
                    -TimeoutSeconds 1

                $stopwatch.Stop()
                $result.TimedOut | Should -BeTrue
                $stopwatch.Elapsed | Should -BeLessThan ([TimeSpan]::FromMilliseconds(500))
                Should -Invoke Stop-SmartGuardCapturedProcessTree -Times 1 -Exactly
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'missing or malformed TRX never becomes a passed project result' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-trx-result-' + [guid]::NewGuid().ToString('N'))
            $missingTrxPath = Join-Path $tempDirectory 'missing.trx'
            $malformedTrxPath = Join-Path $tempDirectory 'malformed.trx'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                $missing = Read-SmartGuardTrxResult -Path $missingTrxPath -ExitCode 0
                $missing.Result | Should -Be 'Failed'
                $missing.Error | Should -Match 'TRX'

                [System.IO.File]::WriteAllText($malformedTrxPath, '<TestRun', [System.Text.UTF8Encoding]::new($false))
                $malformed = Read-SmartGuardTrxResult -Path $malformedTrxPath -ExitCode 0
                $malformed.Result | Should -Be 'Failed'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'xUnit skipped tests are counted as skipped from total minus executed' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-trx-skip-' + [guid]::NewGuid().ToString('N'))
            $trxPath = Join-Path $tempDirectory 'skip.trx'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                # xUnit reports a skipped test only as total > executed; Counters.notExecuted stays 0.
                $trx = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Completed">' +
                    '<Counters total="3" executed="2" passed="2" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" notExecuted="0" />' +
                    '</ResultSummary></TestRun>'
                [System.IO.File]::WriteAllText($trxPath, $trx, [System.Text.UTF8Encoding]::new($false))
                $result = Read-SmartGuardTrxResult -Path $trxPath -ExitCode 0
                $result.Result | Should -Be 'Passed'
                $result.PassedCount | Should -Be 2
                $result.SkippedCount | Should -Be 1
                $result.TotalCount | Should -Be 3
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Run-Tests.ps1 fails the parent gate when the dotnet suite is not passed' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8

            $runTests | Should -Match '\$dotNetResult\.Result\s*-ne\s*''Passed'''
            $runTests | Should -Match 'run-tests-.*summary\.json'
            $runTests | Should -Not -Match 'PASSED=\$total FAILED=\$failed'
        }

        It 'dotnet test projects exclude performance tests outside the nightly tier' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')

            $fastProjects = Get-SmartGuardDotNetTestProjectPaths -RepoRoot $root -Tier fast
            $fullProjects = Get-SmartGuardDotNetTestProjectPaths -RepoRoot $root -Tier full
            $nightlyProjects = Get-SmartGuardDotNetTestProjectPaths -RepoRoot $root -Tier nightly

            ($fastProjects -join [Environment]::NewLine) |
                Should -Not -Match 'SmartGuard\.Engine\.PerformanceTests'
            ($fullProjects -join [Environment]::NewLine) |
                Should -Not -Match 'SmartGuard\.Engine\.PerformanceTests'
            ($nightlyProjects -join [Environment]::NewLine) |
                Should -Match 'SmartGuard\.Engine\.PerformanceTests'
        }

        It 'Settings log workbench performance tests stay behind the Performance category filter' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')

            $fullFilter = Get-SmartGuardDotNetTestFilter -Tier full
            $fullFilter | Should -Match 'Category!=Performance'

            $perfTests = Join-Path $root 'Tests\SmartGuard.Settings.Tests\LogWorkbenchPerformanceTests.cs'
            Test-Path -LiteralPath $perfTests | Should -Be $true
            $content = Get-Content -LiteralPath $perfTests -Raw -Encoding UTF8
            $content | Should -Match '\[Trait\("Category",\s*"Performance"\)\]'
        }

        It 'Run-Tests.ps1 delegates dotnet tests to Invoke-SmartGuardDotNetTests.ps1' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Match 'Invoke-SmartGuardDotNetTests\.ps1'
            $runTests | Should -Match 'Invoke-SmartGuardDotNetTestSuite'
        }

        It 'Run-Tests.ps1 honors SMARTGUARD_TEST_TIER for integration gating' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Match 'SMARTGUARD_TEST_TIER'
            $runTests | Should -Match 'Get-SmartGuardTestTier'
        }

        It 'Tray core user flow does not spawn nested dotnet test' {
            $root = Split-Path -Parent $PSScriptRoot
            $flow = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Tests.ps1') -Raw -Encoding UTF8
            $flow | Should -Not -Match 'dotnet test'
        }

        It 'Run-Tests leaves the normal suite in its original process and delegates only the installer flow' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Not -Match 'Test-RunTestsNeedsInstallerElevation'
            $runTests | Should -Not -Match 'Invoke-RunTestsSingleElevation'
            $runTests | Should -Match '\$installerFlowResult\s*=\s*Invoke-PesterFileSingleElevation'
            $runTests | Should -Not -Match '\$installerFlowResult\s*=\s*Invoke-Pester\s+-Path\s+\$installerFlow'
        }

        It 'Run-Tests treats a Pester discovery failure as a failed integration run' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Match '\$installerFlowResult\.Result\s*-ne\s*''Passed'''
            $runTests | Should -Match '\$integrationResult\.Result\s*-ne\s*''Passed'''
        }

        It 'Run-Tests integration contract failures carry the underlying elevation error' {
            # A failed UAC launch used to surface only as
            # "Result=Failed, ExitCode=1, TotalCount=0" with the real
            # launcher error discarded - a diagnostics black hole.
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Match '\$underlyingError\s*=\s*\[string\]\$integrationResult\.Error'
            $runTests | Should -Match '(?s)Tray integration result contract failed.*?\$underlyingSuffix\)'
        }

        It 'Run-Tests process starter scriptblocks stay bound to the script session state' {
            # .GetNewClosure() rebinds the starter to a detached dynamic module
            # where dot-sourced helpers (Test-IsProcessElevated,
            # ConvertTo-SmartGuardQuotedProcessArgument) are invisible unless an
            # earlier session leaked them into the global scope; the tray gate
            # then fails instantly with "Unable to launch elevated Windows
            # PowerShell ... 无法将 ... 识别为 cmdlet".
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runTests | Should -Not -Match '\.GetNewClosure\(\)'
        }

        It 'PowerShell scripts check out with CRLF so PS 5.1 keeps line breaks after Chinese comments' {
            # eol=lf + BOM-less UTF-8 makes Windows PowerShell 5.1 decode .ps1
            # as ANSI/GBK; the trailing byte 0x82 of a Chinese full stop then
            # swallows a bare LF, gluing the next statement into the comment
            # (observed: "$failures = 1" vanished in Invoke-TestStabilityGate
            # on a fresh checkout while the CRLF worktree copy behaved).
            $root = Split-Path -Parent $PSScriptRoot
            $attributes = Get-Content -LiteralPath (Join-Path $root '.gitattributes') -Raw -Encoding UTF8
            $attributes | Should -Match '\*\.ps1\s+text\s+eol=crlf'
            $attributes | Should -Not -Match '\*\.ps1\s+text\s+eol=lf'
        }

        It 'Test-SmartGuardElevatedPesterResult rejects a Passed result with zero discovered tests' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Test-IsProcessElevated.ps1')
            $zeroDiscovery = [pscustomobject]@{
                Result = 'Passed'
                ExitCode = 0
                PassedCount = 0
                FailedCount = 0
                SkippedCount = 0
                TotalCount = 0
                Error = $null
                CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
            }

            Test-SmartGuardElevatedPesterResult -Result $zeroDiscovery |
                Should -BeFalse

            $zeroDiscovery.PSObject.Properties.Remove('CompletedAtUtc')
            Test-SmartGuardElevatedPesterResult -Result $zeroDiscovery |
                Should -BeFalse
        }

        It 'elevated Pester runner records a complete UTF-8 result and never uses an inline command' {
            $root = Split-Path -Parent $PSScriptRoot
            $runnerPath = Join-Path $root 'scripts\Invoke-ElevatedPesterFile.ps1'
            Test-Path -LiteralPath $runnerPath | Should -BeTrue
            $runner = Get-Content -LiteralPath $runnerPath -Raw -Encoding UTF8
            $elevation = Get-Content -LiteralPath (Join-Path $root 'scripts\Test-IsProcessElevated.ps1') -Raw -Encoding UTF8

            $runner | Should -Match 'Invoke-Pester'
            $runner | Should -Match '\bResult\b'
            $runner | Should -Match '\bTotalCount\b'
            $runner | Should -Match 'UTF8Encoding'
            $runner | Should -Match 'CompletedAtUtc'
            ($runner + [Environment]::NewLine + $elevation) |
                Should -Not -Match '(?s)ArgumentList.*?-Command'
        }

        It 'elevated Pester runner initializes UTF-8 for native command output' {
            $root = Split-Path -Parent $PSScriptRoot
            $runner = Get-Content -LiteralPath (
                Join-Path $root 'scripts\Invoke-ElevatedPesterFile.ps1') -Raw -Encoding UTF8

            $runner | Should -Match '\$utf8\s*=\s*\[System\.Text\.UTF8Encoding\]::new\(\$false\)'
            $runner | Should -Match '\[Console\]::OutputEncoding\s*=\s*\$utf8'
            $runner | Should -Match '\[Console\]::InputEncoding\s*=\s*\$utf8'
            $runner | Should -Match '\$OutputEncoding\s*=\s*\$utf8'
            $runner | Should -Match 'chcp\s+65001'
        }

        It 'Tray integration is elevated with an outer lifecycle and exact two-test acceptance' {
            $root = Split-Path -Parent $PSScriptRoot
            $runTests = Get-Content -LiteralPath (Join-Path $root 'Run-Tests.ps1') -Raw -Encoding UTF8
            $runner = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-ElevatedPesterFile.ps1') -Raw -Encoding UTF8
            $flow = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Tests.ps1') -Raw -Encoding UTF8
            $lifecyclePath = Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Lifecycle.ps1'

            Test-Path -LiteralPath $lifecyclePath | Should -BeTrue
            $runTests | Should -Match '\$integrationResult\s*=\s*Invoke-PesterFileSingleElevation'
            $runTests | Should -Match 'TrayCoreUserFlow\.Lifecycle\.ps1'
            $runTests | Should -Match 'LifecycleScriptPath'
            $runTests | Should -Match '\$integrationResult\.ExitCode\s*-ne\s*0'
            $runTests | Should -Match '\$integrationResult\.TotalCount\s*-ne\s*2'
            $runTests | Should -Match '\$integrationResult\.FailedCount\s*-ne\s*0'
            $runner | Should -Match '\[string\]\$LifecycleScriptPath'
            $runner | Should -Match 'Enter-PesterFileLifecycle'
            $runner | Should -Match 'Exit-PesterFileLifecycle'
            $runner | Should -Not -Match 'Enter-SmartGuardTestLifecycle'
            $runner | Should -Not -Match 'Exit-SmartGuardTestLifecycle'
            $flow | Should -Not -Match 'Capture-SmartGuardScheduledTasks'
            $flow | Should -Not -Match 'Restore-SmartGuardScheduledTasks'
        }

        It 'Tray lifecycle keeps capture stop and outer restore in transactional order' {
            $root = Split-Path -Parent $PSScriptRoot
            $lifecyclePath = Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Lifecycle.ps1'
            Test-Path -LiteralPath $lifecyclePath | Should -BeTrue
            . $lifecyclePath
            $script:task5LifecycleEvents = [System.Collections.Generic.List[string]]::new()
            $script:task5LifecycleStopFails = $false

            Mock Capture-SmartGuardScheduledTasks {
                [void]$script:task5LifecycleEvents.Add('Capture')
                return [pscustomobject]@{ Marker = 'snapshot' }
            }
            Mock Stop-SmartGuardForTrayCoreTest {
                [void]$script:task5LifecycleEvents.Add('Stop')
                if ($script:task5LifecycleStopFails) {
                    throw 'controlled stop failure'
                }
            }
            Mock Restore-SmartGuardScheduledTasks {
                param($Snapshot)
                $Snapshot.Marker | Should -Be 'snapshot'
                [void]$script:task5LifecycleEvents.Add('Restore')
            }

            Enter-PesterFileLifecycle
            Exit-PesterFileLifecycle

            $script:task5LifecycleEvents | Should -Be @(
                'Capture'
                'Stop'
                'Stop'
                'Restore'
            )
        }

        It 'Tray lifecycle restores its captured baseline when enter stop fails' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Lifecycle.ps1')
            $script:task5LifecycleEvents = [System.Collections.Generic.List[string]]::new()

            Mock Capture-SmartGuardScheduledTasks {
                [void]$script:task5LifecycleEvents.Add('Capture')
                return [pscustomobject]@{ Marker = 'snapshot' }
            }
            Mock Stop-SmartGuardForTrayCoreTest {
                [void]$script:task5LifecycleEvents.Add('Stop')
                throw 'controlled stop failure'
            }
            Mock Restore-SmartGuardScheduledTasks {
                param($Snapshot)
                $Snapshot.Marker | Should -Be 'snapshot'
                [void]$script:task5LifecycleEvents.Add('Restore')
            }

            { Enter-PesterFileLifecycle } |
                Should -Throw 'controlled stop failure'

            $script:task5LifecycleEvents | Should -Be @(
                'Capture'
                'Stop'
                'Restore'
            )
        }

        It 'elevated Pester runner exits lifecycle before publishing its successful result' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Test-IsProcessElevated.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-lifecycle-success-' + [guid]::NewGuid().ToString('N'))
            $testPath = Join-Path $tempDirectory 'Controlled.Tests.ps1'
            $lifecyclePath = Join-Path $tempDirectory 'Controlled.Lifecycle.ps1'
            $resultPath = Join-Path $tempDirectory 'result.json'
            $transcriptPath = Join-Path $tempDirectory 'transcript.log'
            $eventsPath = Join-Path $tempDirectory 'events.txt'
            $previousEventsPath = $env:SG_TASK5_EVENTS_PATH
            $previousResultPath = $env:SG_TASK5_RESULT_PATH
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                [System.IO.File]::WriteAllText(
                    $testPath,
                    @'
Describe 'controlled lifecycle Pester' {
    It 'runs between enter and exit' {
        [System.IO.File]::AppendAllText(
            $env:SG_TASK5_EVENTS_PATH,
            'Pester|',
            [System.Text.UTF8Encoding]::new($false))
        $true | Should -BeTrue
    }
}
'@,
                    [System.Text.UTF8Encoding]::new($false))
                [System.IO.File]::WriteAllText(
                    $lifecyclePath,
                    @'
function Enter-PesterFileLifecycle {
    [System.IO.File]::AppendAllText(
        $env:SG_TASK5_EVENTS_PATH,
        'Enter|',
        [System.Text.UTF8Encoding]::new($false))
}

function Exit-PesterFileLifecycle {
    if (Test-Path -LiteralPath $env:SG_TASK5_RESULT_PATH) {
        throw 'result was published before lifecycle exit'
    }
    [System.IO.File]::AppendAllText(
        $env:SG_TASK5_EVENTS_PATH,
        'Exit|',
        [System.Text.UTF8Encoding]::new($false))
}
'@,
                    [System.Text.UTF8Encoding]::new($false))
                $env:SG_TASK5_EVENTS_PATH = $eventsPath
                $env:SG_TASK5_RESULT_PATH = $resultPath

                & (Get-WindowsPowerShellExePath) `
                    -NoProfile `
                    -ExecutionPolicy Bypass `
                    -File (Join-Path $root 'scripts\Invoke-ElevatedPesterFile.ps1') `
                    -TestPath $testPath `
                    -ResultPath $resultPath `
                    -TranscriptPath $transcriptPath `
                    -Tier fast `
                    -LifecycleScriptPath $lifecyclePath
                $childExitCode = $LASTEXITCODE
                $diskResult = [System.IO.File]::ReadAllText(
                    $resultPath,
                    [System.Text.Encoding]::UTF8) | ConvertFrom-Json
                $events = [System.IO.File]::ReadAllText(
                    $eventsPath,
                    [System.Text.Encoding]::UTF8)

                $childExitCode | Should -Be 0
                $diskResult.Result | Should -Be 'Passed'
                $diskResult.ExitCode | Should -Be 0
                $events | Should -Be 'Enter|Pester|Exit|'
            }
            finally {
                if ($null -eq $previousEventsPath) {
                    Remove-Item Env:SG_TASK5_EVENTS_PATH -ErrorAction SilentlyContinue
                }
                else {
                    $env:SG_TASK5_EVENTS_PATH = $previousEventsPath
                }
                if ($null -eq $previousResultPath) {
                    Remove-Item Env:SG_TASK5_RESULT_PATH -ErrorAction SilentlyContinue
                }
                else {
                    $env:SG_TASK5_RESULT_PATH = $previousResultPath
                }
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'elevated Pester runner fails its disk result when lifecycle exit fails' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Test-IsProcessElevated.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-lifecycle-exit-failure-' + [guid]::NewGuid().ToString('N'))
            $testPath = Join-Path $tempDirectory 'Controlled.Tests.ps1'
            $lifecyclePath = Join-Path $tempDirectory 'Controlled.Lifecycle.ps1'
            $resultPath = Join-Path $tempDirectory 'result.json'
            $transcriptPath = Join-Path $tempDirectory 'transcript.log'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                [System.IO.File]::WriteAllText(
                    $testPath,
                    "Describe 'controlled exit failure' { It 'passes Pester' { `$true | Should -BeTrue } }",
                    [System.Text.UTF8Encoding]::new($false))
                [System.IO.File]::WriteAllText(
                    $lifecyclePath,
                    @'
function Enter-PesterFileLifecycle {
}

function Exit-PesterFileLifecycle {
    throw 'controlled lifecycle exit failure'
}
'@,
                    [System.Text.UTF8Encoding]::new($false))

                # 子 runner 失败时通过 Write-Error 写 stderr（被测行为）；Pester 5 在 It 内
                # ErrorActionPreference=Stop，PS 5.1 下原生 stderr 经 2>$null 会变成
                # terminating NativeCommandError，必须临时降为 Continue。
                $previousErrorActionPreference = $ErrorActionPreference
                $ErrorActionPreference = 'Continue'
                try {
                    & (Get-WindowsPowerShellExePath) `
                        -NoProfile `
                        -ExecutionPolicy Bypass `
                        -File (Join-Path $root 'scripts\Invoke-ElevatedPesterFile.ps1') `
                        -TestPath $testPath `
                        -ResultPath $resultPath `
                        -TranscriptPath $transcriptPath `
                        -Tier fast `
                        -LifecycleScriptPath $lifecyclePath 2>$null
                    $childExitCode = $LASTEXITCODE
                }
                finally {
                    $ErrorActionPreference = $previousErrorActionPreference
                }
                $diskResult = [System.IO.File]::ReadAllText(
                    $resultPath,
                    [System.Text.Encoding]::UTF8) | ConvertFrom-Json

                $childExitCode | Should -Be 1
                $diskResult.Result | Should -Be 'Failed'
                $diskResult.ExitCode | Should -Be 1
                $diskResult.Error | Should -Match 'controlled lifecycle exit failure'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'stability gate stops at first failure and never retries to green' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-TestStabilityGate.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-stability-gate-' + [guid]::NewGuid().ToString('N'))
            $resultPath = Join-Path $tempDirectory 'stability.json'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                # 第 2 轮固定失败的测试命令替身；第 3 轮若被调用就能“重试变绿”，
                # 门禁必须在第 2 轮后立即停止。
                Mock Invoke-SmartGuardStabilityIteration {
                    param($Project, $Filter, $ResultDirectory, $RunId, $Iteration, $TimeoutSeconds)
                    $failed = $Iteration -eq 2
                    return [pscustomobject]@{
                        Result = if ($failed) { 'Failed' } else { 'Passed' }
                        ExitCode = if ($failed) { 1 } else { 0 }
                        TrxPath = "synthetic-$Iteration.trx"
                        Output = "synthetic-output-$Iteration"
                        Error = if ($failed) { 'synthetic second-iteration failure' } else { '' }
                    }
                }

                $gateResult = Invoke-SmartGuardTestStabilityGate `
                    -Project 'Synthetic.Tests.csproj' `
                    -Iterations 5 `
                    -ResultPath $resultPath

                $gateResult.Result | Should -Be 'Failed'
                $gateResult.Iterations | Should -Be 5
                $gateResult.Completed | Should -Be 2
                $gateResult.Failures | Should -Be 1
                $gateResult.FirstFailure.Iteration | Should -Be 2
                $gateResult.FirstFailure.ExitCode | Should -Be 1
                $gateResult.FirstFailure.TrxPath | Should -Be 'synthetic-2.trx'
                $gateResult.FirstFailure.Output | Should -Be 'synthetic-output-2'
                Should -Invoke Invoke-SmartGuardStabilityIteration -Times 2 -Exactly

                $diskBytes = [System.IO.File]::ReadAllBytes($resultPath)
                ($diskBytes.Length -ge 3 -and $diskBytes[0] -eq 0xEF -and $diskBytes[1] -eq 0xBB -and $diskBytes[2] -eq 0xBF) |
                    Should -BeFalse -Because 'stability result JSON must be UTF-8 without BOM'
                $diskResult = [System.IO.File]::ReadAllText(
                    $resultPath,
                    [System.Text.UTF8Encoding]::new($false)) | ConvertFrom-Json
                $diskResult.Result | Should -Be 'Failed'
                $diskResult.Completed | Should -Be 2
                $diskResult.Failures | Should -Be 1
                $diskResult.FirstFailure.Iteration | Should -Be 2
                $diskResult.CompletedAtUtc | Should -Not -BeNullOrEmpty
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'stability gate success records every iteration trx path' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-TestStabilityGate.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-stability-pass-' + [guid]::NewGuid().ToString('N'))
            $resultPath = Join-Path $tempDirectory 'stability.json'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                Mock Invoke-SmartGuardStabilityIteration {
                    param($Project, $Filter, $ResultDirectory, $RunId, $Iteration, $TimeoutSeconds)
                    return [pscustomobject]@{
                        Result = 'Passed'
                        ExitCode = 0
                        TrxPath = "synthetic-$Iteration.trx"
                        Output = ''
                        Error = ''
                    }
                }

                $gateResult = Invoke-SmartGuardTestStabilityGate `
                    -Project 'Synthetic.Tests.csproj' `
                    -Iterations 3 `
                    -ResultPath $resultPath

                $gateResult.Result | Should -Be 'Passed'
                $gateResult.Completed | Should -Be 3
                $gateResult.Failures | Should -Be 0
                @($gateResult.TrxPaths) | Should -Be @('synthetic-1.trx', 'synthetic-2.trx', 'synthetic-3.trx')
                Should -Invoke Invoke-SmartGuardStabilityIteration -Times 3 -Exactly

                $diskResult = [System.IO.File]::ReadAllText(
                    $resultPath,
                    [System.Text.UTF8Encoding]::new($false)) | ConvertFrom-Json
                $diskResult.Result | Should -Be 'Passed'
                @($diskResult.TrxPaths).Count | Should -Be 3
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'scheduled-task query rejects unknown native failure instead of capturing Missing' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            Mock cmd.exe { $global:LASTEXITCODE = -2147024891; return '' }

            { Get-SmartGuardScheduledTaskSlot -TaskName 'SmartGuard Guardian' } | Should -Throw
        }

        It 'scheduled-task query rejects invalid successful XML instead of capturing Missing' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            Mock cmd.exe { $global:LASTEXITCODE = 0; return '<Task>' }

            { Get-SmartGuardScheduledTaskSlot -TaskName 'SmartGuard Guardian' } | Should -Throw
        }

        It 'scheduled-task query rejects successful empty output instead of capturing Missing' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            Mock cmd.exe { $global:LASTEXITCODE = 0; return '' }

            { Get-SmartGuardScheduledTaskSlot -TaskName 'SmartGuard Guardian' } | Should -Throw
        }

        It 'scheduled-task query rejects an action without a recoverable root' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            Mock cmd.exe { $global:LASTEXITCODE = 0; return '<Task><Actions><Exec><Command>SmartGuard.Engine.exe</Command></Exec></Actions></Task>' }

            { Get-SmartGuardScheduledTaskSlot -TaskName 'SmartGuard Guardian' } | Should -Throw
        }

        It 'scheduled-task query accepts only explicit file-not-found as Missing' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            Mock cmd.exe { $global:LASTEXITCODE = -2147024894; return '' }

            Get-SmartGuardScheduledTaskSlot -TaskName 'SmartGuard Guardian' | Should -Be 'Missing'
            Should -Invoke cmd.exe -Times 1 -Exactly -ParameterFilter { $args -join ' ' -match '/HRESULT' }
        }

        It 'scheduled-task capture records only valid task-backed production process state' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            $productionRoot = Join-Path $TestDrive 'installed'
            $guardianSlot = [pscustomobject]@{
                Command = '"{0}\bin\SmartGuard.Engine.exe" --root "{0}"' -f $productionRoot
                WorkingDirectory = $productionRoot
                Root = $productionRoot
            }
            $traySlot = [pscustomobject]@{
                Command = '"{0}\bin\SmartGuard.Tray.exe" --root "{0}"' -f $productionRoot
                WorkingDirectory = $productionRoot
                Root = $productionRoot
            }

            Mock Get-SmartGuardScheduledTaskSlot {
                param($TaskName)
                if ($TaskName -eq 'SmartGuard Guardian') {
                    return $guardianSlot
                }
                return $traySlot
            }
            Mock Get-Process {
                param($Name)
                if ($Name -eq 'SmartGuard.Engine') {
                    return [pscustomobject]@{
                        Id = 4242
                        Path = Join-Path $productionRoot 'bin\SmartGuard.Engine.exe'
                    }
                }
                return [pscustomobject]@{
                    Id = 4243
                    Path = Join-Path $TestDrive 'other\SmartGuard.Tray.exe'
                }
            }

            $snapshot = Capture-SmartGuardScheduledTasks

            $snapshot.Guardian | Should -Be $guardianSlot
            $snapshot.Tray | Should -Be $traySlot
            $snapshot.GuardianWasRunning | Should -BeTrue
            $snapshot.TrayWasRunning | Should -BeFalse
        }

        It 'restore native commands preserve a root containing spaces as one argument' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            $probe = Join-Path $TestDrive 'root-argument.ps1'
            [IO.File]::WriteAllText($probe, 'param([string]$Root) Write-Output $Root', [Text.UTF8Encoding]::new($false))
            $expected = 'C:\Program Files\SmartGuard'

            $result = Invoke-SmartGuardNativeExe -FilePath (Get-WindowsPowerShellExePath) -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $probe, '-Root', $expected)

            $result.StdOut | Should -Be $expected
        }

        It 'restore keeps valid stopped task slots stopped' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            $productionRoot = Join-Path $TestDrive 'installed'
            $slot = [pscustomobject]@{
                Command = 'SmartGuard'
                WorkingDirectory = $productionRoot
                Root = $productionRoot
            }
            $snapshot = [pscustomobject]@{
                Guardian = $slot
                Tray = $slot
                GuardianWasRunning = $false
                TrayWasRunning = $false
            }

            Mock Test-IsProcessElevated { $true }
            Mock Test-Path { $true }
            Mock Invoke-SmartGuardRestoreInstall {}
            Mock Remove-SmartGuardScheduledTaskSlot {}
            Mock Start-SmartGuardRestoredTasks {}

            Restore-SmartGuardScheduledTasks -Snapshot $snapshot

            Should -Invoke Invoke-SmartGuardRestoreInstall -Times 1 -Exactly
            Should -Invoke Start-SmartGuardRestoredTasks -Times 0 -Exactly
        }

        It 'restore starts only task-backed processes that were running in the snapshot' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            $productionRoot = Join-Path $TestDrive 'installed'
            $slot = [pscustomobject]@{
                Command = 'SmartGuard'
                WorkingDirectory = $productionRoot
                Root = $productionRoot
            }
            $snapshot = [pscustomobject]@{
                Guardian = $slot
                Tray = $slot
                GuardianWasRunning = $true
                TrayWasRunning = $false
            }

            Mock Test-Path { $true }
            Mock Invoke-SmartGuardNativeExe {
                return [pscustomobject]@{
                    ExitCode = 0
                    StdOut = ''
                    StdErr = ''
                }
            }

            Start-SmartGuardRestoredTasks -Root $productionRoot -Snapshot $snapshot

            Should -Invoke Invoke-SmartGuardNativeExe -Times 1 -Exactly -ParameterFilter {
                $ArgumentList -contains 'guardian'
            }
            Should -Invoke Invoke-SmartGuardNativeExe -Times 0 -Exactly -ParameterFilter {
                $ArgumentList -contains 'tray'
            }
        }

        It 'restore removes a task slot that was missing without reviving its orphan process' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            $productionRoot = Join-Path $TestDrive 'installed'
            $guardianSlot = [pscustomobject]@{
                Command = 'SmartGuard'
                WorkingDirectory = $productionRoot
                Root = $productionRoot
            }
            $snapshot = [pscustomobject]@{
                Guardian = $guardianSlot
                Tray = 'Missing'
                GuardianWasRunning = $false
                TrayWasRunning = $false
            }

            Mock Test-IsProcessElevated { $true }
            Mock Test-Path { $true }
            Mock Invoke-SmartGuardRestoreInstall {}
            Mock Remove-SmartGuardScheduledTaskSlot {}
            Mock Start-SmartGuardRestoredTasks {}

            Restore-SmartGuardScheduledTasks -Snapshot $snapshot

            Should -Invoke Remove-SmartGuardScheduledTaskSlot -Times 1 -Exactly -ParameterFilter {
                $TaskName -eq 'SmartGuard Tray'
            }
            Should -Invoke Start-SmartGuardRestoredTasks -Times 0 -Exactly
        }

        It 'missing task baseline cleans orphan processes without recreating or starting tasks' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\Restore-SmartGuardScheduledTasks.ps1')
            $snapshot = [pscustomobject]@{
                Guardian = 'Missing'
                Tray = 'Missing'
                GuardianWasRunning = $false
                TrayWasRunning = $false
            }

            Mock Test-IsProcessElevated { $true }
            Mock Test-Path { $true }
            Mock Invoke-SmartGuardEngineUninstall {}
            Mock Invoke-SmartGuardRestoreInstall {}
            Mock Start-SmartGuardRestoredTasks {}

            Restore-SmartGuardScheduledTasks -Snapshot $snapshot

            Should -Invoke Invoke-SmartGuardEngineUninstall -Times 1 -Exactly
            Should -Invoke Invoke-SmartGuardRestoreInstall -Times 0 -Exactly
            Should -Invoke Start-SmartGuardRestoredTasks -Times 0 -Exactly
        }

        It 'elevated Pester runner publishes discovery container errors in its disk result' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Test-IsProcessElevated.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-elevated-container-' + [guid]::NewGuid().ToString('N'))
            $testPath = Join-Path $tempDirectory 'ContainerFailure.Tests.ps1'
            $resultPath = Join-Path $tempDirectory 'result.json'
            $transcriptPath = Join-Path $tempDirectory 'transcript.log'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                [System.IO.File]::WriteAllText(
                    $testPath,
                    "throw 'controlled container failure'",
                    [System.Text.UTF8Encoding]::new($false))
                $runnerPath = Join-Path $root 'scripts\Invoke-ElevatedPesterFile.ps1'
                & (Get-WindowsPowerShellExePath) `
                    -NoProfile `
                    -ExecutionPolicy Bypass `
                    -File $runnerPath `
                    -TestPath $testPath `
                    -ResultPath $resultPath `
                    -TranscriptPath $transcriptPath `
                    -Tier fast
                $childExitCode = $LASTEXITCODE
                $diskResult = [System.IO.File]::ReadAllText(
                    $resultPath,
                    [System.Text.Encoding]::UTF8) | ConvertFrom-Json

                $childExitCode | Should -Be 1
                $diskResult.ExitCode | Should -Be 1
                $diskResult.Result | Should -Be 'Failed'
                $diskResult.TotalCount | Should -Be 0
                $diskResult.Error | Should -Match 'controlled container failure'
                $diskResult.CompletedAtUtc | Should -Not -BeNullOrEmpty
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Invoke-PesterFileSingleElevation returns a complete successful disk result' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Test-IsProcessElevated.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-elevated-result-' + [guid]::NewGuid().ToString('N'))
            $resultPath = Join-Path $tempDirectory 'result.json'
            $transcriptPath = Join-Path $tempDirectory 'transcript.log'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                Mock Test-IsProcessElevated { $false }
                Mock Start-Process {
                    $payload = [pscustomobject]@{
                        Result = 'Passed'
                        ExitCode = 0
                        PassedCount = 3
                        FailedCount = 0
                        SkippedCount = 0
                        TotalCount = 3
                        Error = $null
                        CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
                    } | ConvertTo-Json -Compress
                    [System.IO.File]::WriteAllText(
                        $resultPath,
                        $payload,
                        [System.Text.UTF8Encoding]::new($false))
                    return [pscustomobject]@{ HasExited = $true; ExitCode = 0 }
                }

                $result = Invoke-PesterFileSingleElevation `
                    -RepoRoot $root `
                    -TestPath (Join-Path $root 'Tests\SmartGuard.Tests.ps1') `
                    -ResultPath $resultPath `
                    -TranscriptPath $transcriptPath `
                    -Tier fast

                $result.Result | Should -Be 'Passed'
                $result.ExitCode | Should -Be 0
                $result.TotalCount | Should -Be 3
                Should -Invoke Start-Process -Times 1 -Exactly -ParameterFilter {
                    $Verb -eq 'RunAs' -and $ArgumentList -contains '-File'
                }
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Invoke-PesterFileSingleElevation fails closed for a failed Pester disk result' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Test-IsProcessElevated.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-elevated-failed-' + [guid]::NewGuid().ToString('N'))
            $resultPath = Join-Path $tempDirectory 'result.json'
            $transcriptPath = Join-Path $tempDirectory 'transcript.log'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                Mock Test-IsProcessElevated { $false }
                Mock Start-Process {
                    $payload = [pscustomobject]@{
                        Result = 'Failed'
                        ExitCode = 1
                        PassedCount = 2
                        FailedCount = 1
                        SkippedCount = 0
                        TotalCount = 3
                        Error = 'intentional failure'
                        CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
                    } | ConvertTo-Json -Compress
                    [System.IO.File]::WriteAllText(
                        $resultPath,
                        $payload,
                        [System.Text.UTF8Encoding]::new($false))
                    return [pscustomobject]@{ HasExited = $true; ExitCode = 1 }
                }

                $result = Invoke-PesterFileSingleElevation `
                    -RepoRoot $root `
                    -TestPath (Join-Path $root 'Tests\SmartGuard.Tests.ps1') `
                    -ResultPath $resultPath `
                    -TranscriptPath $transcriptPath `
                    -Tier fast

                $result.Result | Should -Be 'Failed'
                $result.ExitCode | Should -Be 1
                $result.TotalCount | Should -Be 3
                $result.Error | Should -Match 'intentional failure'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Invoke-PesterFileSingleElevation fails closed for corrupt result JSON' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Test-IsProcessElevated.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-elevated-corrupt-' + [guid]::NewGuid().ToString('N'))
            $resultPath = Join-Path $tempDirectory 'result.json'
            $transcriptPath = Join-Path $tempDirectory 'transcript.log'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                Mock Test-IsProcessElevated { $false }
                Mock Start-Process {
                    [System.IO.File]::WriteAllText(
                        $resultPath,
                        '{not-json',
                        [System.Text.UTF8Encoding]::new($false))
                    return [pscustomobject]@{ HasExited = $true; ExitCode = 1 }
                }

                $result = Invoke-PesterFileSingleElevation `
                    -RepoRoot $root `
                    -TestPath (Join-Path $root 'Tests\SmartGuard.Tests.ps1') `
                    -ResultPath $resultPath `
                    -TranscriptPath $transcriptPath `
                    -Tier fast

                $result.Result | Should -Be 'Failed'
                $result.ExitCode | Should -Be 1
                $result.Error | Should -Match 'JSON'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Invoke-PesterFileSingleElevation fails closed when the child exits without JSON' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Test-IsProcessElevated.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-elevated-no-result-' + [guid]::NewGuid().ToString('N'))
            $resultPath = Join-Path $tempDirectory 'result.json'
            $transcriptPath = Join-Path $tempDirectory 'transcript.log'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                Mock Test-IsProcessElevated { $false }
                Mock Start-Process {
                    return [pscustomobject]@{ HasExited = $true; ExitCode = 17 }
                }

                $result = Invoke-PesterFileSingleElevation `
                    -RepoRoot $root `
                    -TestPath (Join-Path $root 'Tests\SmartGuard.Tests.ps1') `
                    -ResultPath $resultPath `
                    -TranscriptPath $transcriptPath `
                    -Tier fast

                $result.Result | Should -Be 'Failed'
                $result.ExitCode | Should -Be 1
                $result.Error | Should -Match 'without publishing'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Invoke-PesterFileSingleElevation treats UAC cancellation as failure' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Test-IsProcessElevated.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-elevated-cancelled-' + [guid]::NewGuid().ToString('N'))
            $resultPath = Join-Path $tempDirectory 'result.json'
            $transcriptPath = Join-Path $tempDirectory 'transcript.log'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                Mock Test-IsProcessElevated { $false }
                Mock Start-Process {
                    throw [System.ComponentModel.Win32Exception]::new('The operation was canceled by the user.')
                }

                $result = Invoke-PesterFileSingleElevation `
                    -RepoRoot $root `
                    -TestPath (Join-Path $root 'Tests\SmartGuard.Tests.ps1') `
                    -ResultPath $resultPath `
                    -TranscriptPath $transcriptPath `
                    -Tier fast

                $result.Result | Should -Be 'Failed'
                $result.ExitCode | Should -Be 1
                $result.Error | Should -Match 'launch'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Test-SmartGuardInstallerIntegrationEnabled permits the installer flow to own its single UAC hop' {
            $root = Split-Path -Parent $PSScriptRoot
            $helper = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1') -Raw -Encoding UTF8
            $fn = [regex]::Match($helper, '(?s)function Test-SmartGuardInstallerIntegrationEnabled.*?(?=\r?\nfunction |\z)').Value
            $fn | Should -Not -Match 'Test-IsProcessElevated'
            $fn | Should -Not -Match "if\s*\(\s*\`$Tier\s*-eq\s*'nightly'\s*\)\s*\{\s*return\s+\`$true\s*\}"
        }

        It 'InstallerUserFlow.Tests.ps1 self-handoffs to one elevated Pester run when launched directly' {
            $root = Split-Path -Parent $PSScriptRoot
            $elevation = Get-Content -LiteralPath (Join-Path $root 'scripts\Test-IsProcessElevated.ps1') -Raw -Encoding UTF8
            $runnerPath = Join-Path $root 'scripts\Invoke-ElevatedInstallerUserFlow.ps1'
            $flow = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\InstallerUserFlow.Tests.ps1') -Raw -Encoding UTF8
            $elevation | Should -Match 'function Invoke-InstallerUserFlowSingleElevation'
            $elevation | Should -Match 'function Invoke-PesterFileSingleElevation'
            $elevation | Should -Match 'SMARTGUARD_ELEVATED_RUN'
            $elevation | Should -Match '\$powershellExe\s*=\s*Get-WindowsPowerShellExePath'
            $elevation | Should -Match 'Start-Process\s+-FilePath\s+\$powershellExe\s+-Verb\s+RunAs'
            $elevation | Should -Not -Match 'Verb\s+RunAs\s+-Wait'
            $elevation | Should -Match "'-File'"
            $elevation | Should -Match 'Invoke-ElevatedPesterFile\.ps1'
            $elevation | Should -Match "'-ResultPath'"
            $elevation | Should -Match 'Start-Sleep\s+-Milliseconds\s+250'
            $elevation | Should -Match 'AddMinutes\(10\)'
            $elevation | Should -Match 'CompletedAtUtc'
            $elevation | Should -Match 'ConvertFrom-Json'
            $elevation | Should -Match 'Unable to launch elevated Windows PowerShell'
            Test-Path -LiteralPath $runnerPath | Should -BeTrue
            $runner = Get-Content -LiteralPath $runnerPath -Raw -Encoding UTF8
            $runner | Should -Match 'Invoke-ElevatedPesterFile\.ps1'
            $runner | Should -Not -Match 'Invoke-Pester'
            $flow | Should -Match 'Invoke-InstallerUserFlowSingleElevation'
            $flow | Should -Match '\$PSCommandPath'
            $flow | Should -Match '\$installerFlowHandoff\.HandedOff'
        }

        It 'dotnet test helper builds with no-restore before no-build test fanout' {
            $root = Split-Path -Parent $PSScriptRoot
            $helper = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1') -Raw -Encoding UTF8
            $helper | Should -Match 'dotnet build'
            $helper | Should -Match '--no-restore'
            $helper | Should -Match '--no-build'
        }

        It 'parallel dotnet test processes are polled and time out instead of blocking on background jobs' {
            $root = Split-Path -Parent $PSScriptRoot
            $helper = Get-Content -LiteralPath (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1') -Raw -Encoding UTF8
            $helper | Should -Match 'function Start-SmartGuardCapturedCommand'
            $helper | Should -Match 'function Complete-SmartGuardCapturedCommand'
            $helper | Should -Match 'Start-Process[\s\S]*?-FilePath\s+\$env:ComSpec'
            $helper | Should -Match 'dotnet-test-\$runId\.stdout\.log'
            $helper | Should -Not -Match '%ERRORLEVEL%'
            $helper | Should -Not -Match '\.ExitPath'
            $helper | Should -Match '\$Entry\.Process\.HasExited'
            $helper | Should -Match '\$Entry\.Process\.WaitForExit\(\)'
            $helper | Should -Match '\$Entry\.Process\.ExitCode'
            $helper | Should -Match 'function Read-SmartGuardTextFileWhenUnlocked'
            $helper | Should -Match 'catch \[System\.IO\.IOException\]'
            $helper | Should -Match 'Read-SmartGuardTextFileWhenUnlocked -Path \$Entry\.StdoutPath'
            $helper | Should -Match 'Stop-SmartGuardCapturedProcessTree\s+-ProcessId'
            $helper | Should -Match 'Start-Sleep\s+-Milliseconds'
            $helper | Should -Not -Match 'Wait-Job'
            $helper | Should -Match 'Invoke-SmartGuardDotNetTestProcesses[\s\S]*?-ProjectPaths\s+@\(\$project\)'
        }

        It 'dotnet test output reader tolerates a writer that releases after the old five second window' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-output-lock-' + [guid]::NewGuid().ToString('N'))
            $path = Join-Path $tempDirectory 'stdout.log'
            $readyPath = Join-Path $tempDirectory 'ready'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            [System.IO.File]::WriteAllText($path, 'completed output', [System.Text.UTF8Encoding]::new($false))
            $holder = Start-Job -ScriptBlock {
                param($lockedPath, $signalPath)
                $stream = [System.IO.File]::Open(
                    $lockedPath,
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::ReadWrite,
                    [System.IO.FileShare]::None)
                try {
                    [System.IO.File]::WriteAllText($signalPath, 'ready')
                    Start-Sleep -Milliseconds 6500
                }
                finally {
                    $stream.Dispose()
                }
            } -ArgumentList $path, $readyPath
            try {
                $deadline = [DateTime]::UtcNow.AddSeconds(5)
                while (-not (Test-Path -LiteralPath $readyPath) -and [DateTime]::UtcNow -lt $deadline) {
                    Start-Sleep -Milliseconds 25
                }
                Test-Path -LiteralPath $readyPath | Should -BeTrue

                Read-SmartGuardTextFileWhenUnlocked -Path $path | Should -Be 'completed output'
            }
            finally {
                Wait-Job -Job $holder -Timeout 10 | Out-Null
                Remove-Job -Job $holder -Force
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'dotnet test output reader reads while a lingering writer still holds the write handle' {
            # MSBuild nodeReuse worker processes inherit the redirected stdout
            # handle (FileShare Read+Write) and keep it open long after dotnet
            # test exits; the reader must not wait for such writers to go away.
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'scripts\Invoke-SmartGuardDotNetTests.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-output-writer-' + [guid]::NewGuid().ToString('N'))
            $path = Join-Path $tempDirectory 'stdout.log'
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            [System.IO.File]::WriteAllText($path, 'completed output', [System.Text.UTF8Encoding]::new($false))
            $writer = [System.IO.File]::Open(
                $path,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::ReadWrite)
            try {
                $elapsed = [System.Diagnostics.Stopwatch]::StartNew()
                Read-SmartGuardTextFileWhenUnlocked -Path $path -TimeoutMilliseconds 3000 | Should -Be 'completed output'
                $elapsed.Stop()
                $elapsed.ElapsedMilliseconds | Should -BeLessThan 1000
            }
            finally {
                $writer.Dispose()
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'InstallerUserFlow.Tests.ps1 re-resolves repo root in BeforeAll when script scope is empty' {
            $root = Split-Path -Parent $PSScriptRoot
            $flow = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\InstallerUserFlow.Tests.ps1') -Raw -Encoding UTF8
            $flow | Should -Match 'IsNullOrWhiteSpace\(\$script:InstallerUserFlowRepoRoot\)'
            $flow | Should -Match '\$script:InstallerUserFlowRepoRoot = \(Resolve-Path \(Join-Path \$PSScriptRoot ''\.\.\\\.\.''\)\)\.Path'
        }

        It 'Stop-SmartGuardProcesses avoids Engine --uninstall UAC when not elevated' {
            $root = Split-Path -Parent $PSScriptRoot
            $stop = Get-Content -LiteralPath (Join-Path $root 'Tests\Integration\SmartGuardStop.ps1') -Raw -Encoding UTF8
            $stop | Should -Match 'Stop-SmartGuardProcessesWithoutElevation'
            $stop | Should -Match 'if\s*\(\s*-not\s*\(\s*Test-IsProcessElevated\s*\)\s*\)'
        }

        It 'Stop-SmartGuardProcessesWithoutElevation reports every inaccessible SmartGuard process with recovery guidance' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\SmartGuardStop.ps1')

            Mock Get-Process {
                param($Id, $Name)
                $processes = @(
                    [pscustomobject]@{ Id = 4242; ProcessName = 'SmartGuard.Engine' }
                    [pscustomobject]@{ Id = 4243; ProcessName = 'SmartGuard.Tray' }
                )

                if ($PSBoundParameters.ContainsKey('Id')) {
                    return @($processes | Where-Object Id -eq $Id)
                }

                if ((@($Name) -join ',') -ne 'SmartGuard.Engine,SmartGuard.Tray') {
                    throw "Unexpected process-name query: $($Name -join ',')"
                }

                return $processes
            }
            Mock Stop-Process {
                param($Id)
                throw [System.UnauthorizedAccessException]::new("Access denied $Id")
            }
            Mock Test-IsProcessElevated { $false }

            $exception = $null
            try {
                Stop-SmartGuardProcessesWithoutElevation -ProcessId 4242 -TimeoutMs 0
            }
            catch {
                $exception = $_.Exception
            }

            $exception | Should -Not -BeNullOrEmpty
            $exception.Message | Should -Match '4242'
            $exception.Message | Should -Match 'SmartGuard\.Engine'
            $exception.Message | Should -Match '4243'
            $exception.Message | Should -Match 'SmartGuard\.Tray'
            $exception.Message | Should -Match 'CurrentProcessElevated=False'
            $exception.Message | Should -Match 'StopFailures=.*PID=4242, Name=SmartGuard\.Engine, Error=Access denied 4242'
            $exception.Message | Should -Match 'StopFailures=.*PID=4243, Name=SmartGuard\.Tray, Error=Access denied 4243'
            $exception.Message | Should -Match 'administrator'
            $exception.Message | Should -Match 'complete test gate'
            Should -Invoke Stop-Process -Times 1 -Exactly -ParameterFilter { $Id -eq 4242 }
            Should -Invoke Stop-Process -Times 1 -Exactly -ParameterFilter { $Id -eq 4243 }
        }

        It 'Stop-SmartGuardProcessesWithoutElevation produces no output when every target exits' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\SmartGuardStop.ps1')
            $script:task4TargetAlive = $true

            Mock Get-Process {
                param($Id, $Name)
                if ($PSBoundParameters.ContainsKey('Id') -and $script:task4TargetAlive) {
                    return [pscustomobject]@{ Id = 4242; ProcessName = 'SmartGuard.Engine' }
                }
                return @()
            }
            Mock Stop-Process { $script:task4TargetAlive = $false }

            $result = @(Stop-SmartGuardProcessesWithoutElevation -ProcessId 4242 -TimeoutMs 0)

            $result | Should -BeNullOrEmpty
            Should -Invoke Stop-Process -Times 1 -Exactly
        }

        It 'Assert-SmartGuardProcessesExited clamps polling to the remaining timeout budget' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\SmartGuardStop.ps1')
            $script:task4SleepRequests = @()

            Mock Get-Process {
                param($Id, $Name)
                if ($PSBoundParameters.ContainsKey('Id')) {
                    return [pscustomobject]@{ Id = 4242; ProcessName = 'SmartGuard.Engine' }
                }
                return @()
            }
            Mock Start-Sleep {
                param([int]$Milliseconds)
                $script:task4SleepRequests += $Milliseconds
            }

            { Assert-SmartGuardProcessesExited -ProcessId 4242 -TimeoutMs 20 -PollMs 5000 } |
                Should -Throw 'SmartGuard processes did not exit within 20 ms*'

            $script:task4SleepRequests[0] | Should -BeLessOrEqual 20
        }

        It 'Wait-StatusFile returns an existing status payload even after the engine exits' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Helpers.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-status-existing-' + [guid]::NewGuid().ToString('N'))
            $statusPath = Join-Path $tempDirectory 'SmartGuard.status.json'
            $process = [pscustomobject]@{ Id = 4242; HasExited = $true; ExitCode = 17 }
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                [System.IO.File]::WriteAllText(
                    $statusPath,
                    '{"currentPlan":"Balanced"}',
                    [System.Text.UTF8Encoding]::new($false))

                $payload = Wait-StatusFile -StatusPath $statusPath -Process $process

                $payload.currentPlan | Should -Be 'Balanced'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Wait-StatusFile reports an exited engine with the final UTF-8 startup-log tail before the status timeout' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Helpers.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-status-tail-' + [guid]::NewGuid().ToString('N'))
            $statusPath = Join-Path $tempDirectory 'SmartGuard.status.json'
            $startupLogPath = Join-Path $tempDirectory 'SmartGuard.startup.log'
            $process = [pscustomobject]@{
                Id = 4242
                HasExited = $true
                ExitCode = 17
            }
            Mock Start-Sleep {}
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                [System.IO.File]::WriteAllLines(
                    $startupLogPath,
                    @(1..30 | ForEach-Object { "startup-line-$_" }),
                    [System.Text.UTF8Encoding]::new($false))

                $exception = $null
                try {
                    Wait-StatusFile -StatusPath $statusPath -Process $process -StartupLogPath $startupLogPath -TimeoutSec 25
                }
                catch {
                    $exception = $_.Exception
                }

                $exception | Should -Not -BeNullOrEmpty
                $exception.Message | Should -Match '4242'
                $exception.Message | Should -Match '17'
                $exception.Message | Should -Match 'SmartGuard\.status\.json'
                $exception.Message | Should -Match 'StartupLogTail'
                $exception.Message | Should -Match 'single instance|startup failure'
                $exception.Message | Should -Match 'startup-line-11'
                $exception.Message | Should -Match 'startup-line-30'
                $exception.Message | Should -Not -Match 'startup-line-10'
                Should -Invoke Start-Sleep -Times 0 -Exactly
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Wait-StatusFile throws with startup-log diagnostics when a live engine never publishes status' {
            # The timeout path used to `return $null`, so the Pester assertion
            # only said "Expected a value, but got $null" with zero evidence
            # (no PID, no startup log tail). Timeouts must carry diagnostics.
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Helpers.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-status-timeout-' + [guid]::NewGuid().ToString('N'))
            $statusPath = Join-Path $tempDirectory 'SmartGuard.status.json'
            $startupLogPath = Join-Path $tempDirectory 'SmartGuard.startup.log'
            $process = [pscustomobject]@{
                Id = 5151
                HasExited = $false
                ExitCode = $null
            }
            Mock Start-Sleep {}
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            try {
                [System.IO.File]::WriteAllLines(
                    $startupLogPath,
                    @('startup-slow-line-1', 'startup-slow-line-2'),
                    [System.Text.UTF8Encoding]::new($false))

                $exception = $null
                try {
                    Wait-StatusFile -StatusPath $statusPath -Process $process -StartupLogPath $startupLogPath -TimeoutSec 0
                }
                catch {
                    $exception = $_.Exception
                }

                $exception | Should -Not -BeNullOrEmpty
                $exception.Message | Should -Match 'Timed out'
                $exception.Message | Should -Match '5151'
                $exception.Message | Should -Match 'still running'
                $exception.Message | Should -Match 'SmartGuard\.status\.json'
                $exception.Message | Should -Match 'startup-slow-line-2'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Wait-StatusFile keeps waiting while antivirus-style init delays the startup log beyond the status timeout' {
            # Elevated runs showed the engine alive for 25s with NO startup log
            # at all: the process was stuck in OS/AV image scanning before any
            # application code ran. The status timeout must start counting from
            # the moment the startup log appears (application code running),
            # not from process creation, otherwise cold AV scans fail the gate.
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\TrayCoreUserFlow.Helpers.ps1')
            $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-status-init-' + [guid]::NewGuid().ToString('N'))
            $statusPath = Join-Path $tempDirectory 'SmartGuard.status.json'
            $startupLogPath = Join-Path $tempDirectory 'SmartGuard.startup.log'
            $process = [pscustomobject]@{
                Id = 6161
                HasExited = $false
                ExitCode = $null
            }
            [void](New-Item -ItemType Directory -Path $tempDirectory)
            $script:InitDelaySleepCount = 0
            Mock Start-Sleep {
                $script:InitDelaySleepCount++
                if ($script:InitDelaySleepCount -eq 2) {
                    [System.IO.File]::WriteAllLines(
                        $startupLogPath,
                        @('late-startup-line'),
                        [System.Text.UTF8Encoding]::new($false))
                    [System.IO.File]::WriteAllText(
                        $statusPath,
                        '{"currentPlan":"Balanced"}',
                        [System.Text.UTF8Encoding]::new($false))
                }
            }
            try {
                $payload = Wait-StatusFile -StatusPath $statusPath -Process $process -StartupLogPath $startupLogPath -TimeoutSec 0 -InitTimeoutSec 30
                $payload | Should -Not -BeNullOrEmpty
                $payload.currentPlan | Should -Be 'Balanced'
            }
            finally {
                Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Describe 'Integration engine roots are read-only for this machine' {
        It 'seeds the init marker and a paused config before an integration engine starts' {
            $root = Split-Path -Parent $PSScriptRoot
            . (Join-Path $root 'Tests\Integration\ReadOnlyEngineRoot.ps1')
            $engineRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('sg-readonly-root-' + [guid]::NewGuid().ToString('N'))
            try {
                Initialize-SmartGuardReadOnlyEngineRoot -Root $engineRoot

                # The marker skips the one-time brightness alignment (the capability probe only reads).
                Test-Path -LiteralPath (Join-Path $engineRoot $SmartGuardInitMarkerFileName) | Should -Be $true
                # A paused config keeps the policy from switching the active plan.
                $configPath = Join-Path $engineRoot $SmartGuardConfigFileName
                $bytes = [System.IO.File]::ReadAllBytes($configPath)
                ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) | Should -Be $false
                $config = [System.Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
                $config.Paused | Should -Be $true
                $config.ActivePlanGUID | Should -Be '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
                $config.BalancedPlanGUID | Should -Be '381b4222-f694-41f0-9685-ff5bb260df2e'
                $config.PowerSaverPlanGUID | Should -Be 'a1841308-3541-4fab-bc81-f71556f20b4a'
            }
            finally {
                Remove-Item -LiteralPath $engineRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
