$testPath = Join-Path $PSScriptRoot 'Tests\SmartGuard.Tests.ps1'
$resultPath = Join-Path $PSScriptRoot 'test-result.txt'

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

$pester = Get-Module -ListAvailable -Name Pester | Sort-Object Version -Descending | Select-Object -First 1
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

$tier = Get-SmartGuardTestTier
Write-Host "SmartGuard test tier: $tier"

$r = Invoke-Pester -Path $testPath -PassThru
if ($r.Result -ne 'Passed') {
    Write-Host "Pester FAILED=$($r.FailedCount)" -ForegroundColor Red
    exit 1
}

if (-not (Invoke-SmartGuardDotNetTestSuite -RepoRoot $PSScriptRoot -Tier $tier)) {
    exit 1
}

$integrationResult = [pscustomobject]@{ PassedCount = 0; FailedCount = 0 }
$installerFlowResult = [pscustomobject]@{ PassedCount = 0; FailedCount = 0 }
$integration = Join-Path $PSScriptRoot 'Tests\Integration\TrayCoreUserFlow.Tests.ps1'
if ((Test-Path -LiteralPath $integration) -and (Test-SmartGuardTrayIntegrationEnabled -Tier $tier)) {
    Write-Host 'Running integration: Tray core user flow...'
    $integrationResult = Invoke-Pester -Path $integration -PassThru
    if ($integrationResult.Result -ne 'Passed') {
        Write-Host "Integration FAILED=$($integrationResult.FailedCount)" -ForegroundColor Red
        exit 1
    }
}
elseif (Test-Path -LiteralPath $integration) {
    Write-Host "Skipping tray integration: SMARTGUARD_TEST_TIER=$tier" -ForegroundColor Yellow
}

$installerFlow = Join-Path $PSScriptRoot 'Tests\Integration\InstallerUserFlow.Tests.ps1'
if (Test-Path -LiteralPath $installerFlow) {
    if (Test-SmartGuardInstallerIntegrationEnabled -Tier $tier) {
        Write-Host 'Running integration: Installer user flow...'
        $installerFlowResult = Invoke-Pester -Path $installerFlow -PassThru
        if ($installerFlowResult.Result -ne 'Passed') {
            Write-Host "Installer integration FAILED=$($installerFlowResult.FailedCount)" -ForegroundColor Red
            exit 1
        }
    }
    else {
        $reason = if ($tier -eq 'fast') {
            "SMARTGUARD_TEST_TIER=$tier"
        }
        elseif ($env:SMARTGUARD_SKIP_INSTALLER_TESTS -eq '1') {
            'SMARTGUARD_SKIP_INSTALLER_TESTS=1'
        }
        else {
            'disabled'
        }
        Write-Host "Skipping installer integration: $reason" -ForegroundColor Yellow
    }
}
$total = $r.PassedCount + $integrationResult.PassedCount + $installerFlowResult.PassedCount
$failed = $r.FailedCount + $integrationResult.FailedCount + $installerFlowResult.FailedCount
Write-Host "PASSED=$total FAILED=$failed TOTAL=$($total + $failed)"
if ($failed -gt 0) { exit 1 }
