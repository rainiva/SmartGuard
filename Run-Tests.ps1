$testPath = Join-Path $PSScriptRoot 'Tests\SmartGuard.Tests.ps1'
$resultDirectory = Join-Path $PSScriptRoot '.work\test-results'
$runId = '{0}-{1}' -f (
    [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')),
    ([guid]::NewGuid().ToString('N'))
$summaryPath = Join-Path $resultDirectory "run-tests-$runId.summary.json"

# 确保 UTF-8 编码
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$env:DOTNET_CONSOLE_ENCODING = 'utf-8'
if ((chcp) -notmatch '65001') {
    chcp 65001 | Out-Null
}

. (Join-Path $PSScriptRoot 'scripts\Test-IsProcessElevated.ps1')
. (Join-Path $PSScriptRoot 'scripts\Invoke-SmartGuardDotNetTests.ps1')

function New-SmartGuardTestLayerResult {
    param(
        [string]$Result = 'NotRun',
        [string]$Error = ''
    )

    return [pscustomobject]@{
        Result = $Result
        PassedCount = 0
        FailedCount = 0
        SkippedCount = 0
        TotalCount = 0
        Error = $Error
    }
}

function ConvertTo-SmartGuardPesterLayerResult {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$PesterResult
    )

    return [pscustomobject]@{
        Result = $PesterResult.Result.ToString()
        PassedCount = [int]$PesterResult.PassedCount
        FailedCount = [int]$PesterResult.FailedCount
        SkippedCount = [int]$PesterResult.SkippedCount
        TotalCount = [int]$PesterResult.TotalCount
        Error = ''
    }
}

function Write-SmartGuardTestLayerSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [psobject]$Layer
    )

    $color = if ($Layer.Result -eq 'Passed') {
        'Green'
    }
    elseif ($Layer.Result -eq 'Skipped') {
        'Yellow'
    }
    else {
        'Red'
    }
    Write-Host (
        '{0} Result={1} Passed={2} Failed={3} Skipped={4} Total={5}' -f
        $Name,
        $Layer.Result,
        $Layer.PassedCount,
        $Layer.FailedCount,
        $Layer.SkippedCount,
        $Layer.TotalCount) -ForegroundColor $color
}

$pester = Get-Module -ListAvailable -Name Pester |
    Sort-Object Version -Descending |
    Select-Object -First 1
if (-not $pester -or $pester.Version -lt [version]'5.0.0') {
    Write-Host 'Installing Pester 5.x...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    if (-not (Get-PackageProvider -Name NuGet -ErrorAction SilentlyContinue)) {
        Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force
    }
    Set-PSRepository -Name PSGallery -InstallationPolicy Trusted -ErrorAction SilentlyContinue
    Install-Module -Name Pester -Scope CurrentUser -Force -SkipPublisherCheck -AllowClobber
}

Import-Module Pester -MinimumVersion 5.0 -Force

if (-not (Test-Path -LiteralPath $testPath)) {
    Write-Error "Missing: $testPath"
    exit 1
}

[void](New-Item -ItemType Directory -Path $resultDirectory -Force)
$tier = Get-SmartGuardTestTier
Write-Host "SmartGuard test tier: $tier"

$pesterResult = New-SmartGuardTestLayerResult -Result 'Failed'
$dotNetResult = New-SmartGuardTestLayerResult
$trayIntegrationLayer = New-SmartGuardTestLayerResult
$installerIntegrationLayer = New-SmartGuardTestLayerResult

try {
    $r = Invoke-Pester -Path $testPath -PassThru
    $pesterResult = ConvertTo-SmartGuardPesterLayerResult -PesterResult $r
}
catch {
    $pesterResult.Error = $_.Exception.Message
}

if ($pesterResult.Result -eq 'Passed') {
    try {
        $dotNetResult = Invoke-SmartGuardDotNetTestSuite -RepoRoot $PSScriptRoot -Tier $tier
    }
    catch {
        $dotNetResult = New-SmartGuardTestLayerResult -Result 'Failed' -Error $_.Exception.Message
    }
}
else {
    $dotNetResult.Error = 'Skipped because the Pester layer did not pass.'
}

if ($dotNetResult.Result -ne 'Passed') {
    Write-Host "Dotnet suite did not pass: $($dotNetResult.Error)" -ForegroundColor Red
}

$integrationResult = New-SmartGuardTestLayerResult
$integration = Join-Path $PSScriptRoot 'Tests\Integration\TrayCoreUserFlow.Tests.ps1'
$integrationLifecycle =
    Join-Path $PSScriptRoot 'Tests\Integration\TrayCoreUserFlow.Lifecycle.ps1'
$integrationResultPath =
    Join-Path $resultDirectory "tray-core-user-flow-elevated-$runId.result.json"
$integrationTranscriptPath =
    Join-Path $resultDirectory "tray-core-user-flow-elevated-$runId.transcript.log"
if ($dotNetResult.Result -eq 'Passed' -and
    (Test-Path -LiteralPath $integration) -and
    (Test-SmartGuardTrayIntegrationEnabled -Tier $tier)) {
    Write-Host 'Running integration: Tray core user flow...'
    try {
        $startTrayIntegrationProcess = {
            param(
                [string]$ResolvedPowerShellExe,
                [object[]]$ResolvedArgumentList
            )

            $arguments = @($ResolvedArgumentList) + @(
                '-LifecycleScriptPath',
                (ConvertTo-SmartGuardQuotedProcessArgument -Value $integrationLifecycle)
            )
            if (Test-IsProcessElevated) {
                return Start-Process `
                    -FilePath $ResolvedPowerShellExe `
                    -PassThru `
                    -ArgumentList $arguments
            }

            return Start-Process `
                -FilePath $ResolvedPowerShellExe `
                -Verb RunAs `
                -PassThru `
                -ArgumentList $arguments
        }.GetNewClosure()

        $integrationResult = Invoke-PesterFileSingleElevation `
            -RepoRoot $PSScriptRoot `
            -TestPath $integration `
            -ResultPath $integrationResultPath `
            -TranscriptPath $integrationTranscriptPath `
            -Tier $tier `
            -ProcessStarter $startTrayIntegrationProcess
        $trayIntegrationLayer = ConvertTo-SmartGuardPesterLayerResult -PesterResult $integrationResult
        if ($integrationResult.Result -ne 'Passed' -or
            [int]$integrationResult.ExitCode -ne 0 -or
            [int]$integrationResult.TotalCount -ne 2 -or
            [int]$integrationResult.FailedCount -ne 0) {
            $trayIntegrationLayer.Result = 'Failed'
            $trayIntegrationLayer.Error = (
                'Tray integration result contract failed: ' +
                "Result=$($integrationResult.Result), " +
                "ExitCode=$($integrationResult.ExitCode), " +
                "TotalCount=$($integrationResult.TotalCount), " +
                "FailedCount=$($integrationResult.FailedCount).")
        }
    }
    catch {
        $trayIntegrationLayer = New-SmartGuardTestLayerResult -Result 'Failed' -Error $_.Exception.Message
    }
}
elseif (-not (Test-Path -LiteralPath $integration)) {
    $trayIntegrationLayer = New-SmartGuardTestLayerResult -Result 'Skipped' -Error 'Tray integration file is missing.'
}
elseif (-not (Test-SmartGuardTrayIntegrationEnabled -Tier $tier)) {
    $trayIntegrationLayer = New-SmartGuardTestLayerResult -Result 'Skipped' -Error "SMARTGUARD_TEST_TIER=$tier"
}
else {
    $trayIntegrationLayer.Error = 'Skipped because an upstream layer did not pass.'
}

$installerFlowResult = New-SmartGuardTestLayerResult
$installerFlow = Join-Path $PSScriptRoot 'Tests\Integration\InstallerUserFlow.Tests.ps1'
$installerFlowResultPath =
    Join-Path $resultDirectory "installer-user-flow-elevated-$runId.result.json"
$installerFlowTranscriptPath =
    Join-Path $resultDirectory "installer-user-flow-elevated-$runId.transcript.log"
$trayAllowsInstaller = $trayIntegrationLayer.Result -in @('Passed', 'Skipped')
if ($dotNetResult.Result -eq 'Passed' -and
    $trayAllowsInstaller -and
    (Test-Path -LiteralPath $installerFlow) -and
    (Test-SmartGuardInstallerIntegrationEnabled -Tier $tier)) {
    Write-Host 'Running integration: Installer user flow...'
    try {
        $installerFlowResult = Invoke-PesterFileSingleElevation `
            -RepoRoot $PSScriptRoot `
            -TestPath $installerFlow `
            -ResultPath $installerFlowResultPath `
            -TranscriptPath $installerFlowTranscriptPath `
            -Tier $tier
        $installerIntegrationLayer =
            ConvertTo-SmartGuardPesterLayerResult -PesterResult $installerFlowResult
        if ($installerFlowResult.Result -ne 'Passed') {
            $installerIntegrationLayer.Error = [string]$installerFlowResult.Error
            if ([string]::IsNullOrWhiteSpace($installerIntegrationLayer.Error)) {
                $installerIntegrationLayer.Error =
                    'Installer integration Pester result was not Passed.'
            }
        }
    }
    catch {
        $installerIntegrationLayer =
            New-SmartGuardTestLayerResult -Result 'Failed' -Error $_.Exception.Message
    }
}
elseif (-not (Test-Path -LiteralPath $installerFlow)) {
    $installerIntegrationLayer =
        New-SmartGuardTestLayerResult -Result 'Skipped' -Error 'Installer integration file is missing.'
}
elseif (-not (Test-SmartGuardInstallerIntegrationEnabled -Tier $tier)) {
    $reason = if ($tier -eq 'fast') {
        "SMARTGUARD_TEST_TIER=$tier"
    }
    elseif ($env:SMARTGUARD_SKIP_INSTALLER_TESTS -eq '1') {
        'SMARTGUARD_SKIP_INSTALLER_TESTS=1'
    }
    else {
        'disabled'
    }
    $installerIntegrationLayer =
        New-SmartGuardTestLayerResult -Result 'Skipped' -Error $reason
}
else {
    $installerIntegrationLayer.Error = 'Skipped because an upstream layer did not pass.'
}

$exitCode = 0
if ($pesterResult.Result -ne 'Passed') {
    $exitCode = 1
}
if ($dotNetResult.Result -ne 'Passed') {
    $exitCode = 1
}
if ($trayIntegrationLayer.Result -notin @('Passed', 'Skipped')) {
    $exitCode = 1
}
if ($installerIntegrationLayer.Result -notin @('Passed', 'Skipped')) {
    $exitCode = 1
}
$result = if ($exitCode -eq 0) { 'Passed' } else { 'Failed' }
$trxPaths = @(
    $dotNetResult.ProjectResults |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.TrxPath) } |
        ForEach-Object { $_.TrxPath }
)

$summary = [ordered]@{
    RunId = $runId
    Tier = $tier
    Result = $result
    Pester = $pesterResult
    DotNet = $dotNetResult
    TrayIntegration = $trayIntegrationLayer
    InstallerIntegration = $installerIntegrationLayer
    ExitCode = $exitCode
    TrxPaths = $trxPaths
    CompletedAtUtc = [DateTime]::UtcNow.ToString('o')
}
$summaryJson = $summary | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText(
    $summaryPath,
    $summaryJson,
    [System.Text.UTF8Encoding]::new($false))

Write-SmartGuardTestLayerSummary -Name 'PESTER' -Layer $pesterResult
Write-SmartGuardTestLayerSummary -Name 'DOTNET' -Layer $dotNetResult
Write-SmartGuardTestLayerSummary -Name 'TRAY_INTEGRATION' -Layer $trayIntegrationLayer
Write-SmartGuardTestLayerSummary -Name 'INSTALLER_INTEGRATION' -Layer $installerIntegrationLayer
Write-Host "RESULT Tier=$tier Result=$result ExitCode=$exitCode Summary=$summaryPath"
exit $exitCode
