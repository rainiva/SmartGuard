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
if (Test-RunTestsNeedsInstallerElevation -RepoRoot $PSScriptRoot) {
    Invoke-RunTestsSingleElevation -RunTestsScript (Join-Path $PSScriptRoot 'Run-Tests.ps1')
}

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

$r = Invoke-Pester -Path $testPath -PassThru
$engineTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj'
$configTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj'
$archTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.Architecture.Tests\SmartGuard.Architecture.Tests.csproj'
$trayTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.Tray.Tests\SmartGuard.Tray.Tests.csproj'
$logViewerTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.LogViewer.Tests\SmartGuard.LogViewer.Tests.csproj'
$settingsTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj'
$dotnet = dotnet test $engineTests --nologo -v q 2>&1
$dotnet | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host 'dotnet test (Engine) FAILED' -ForegroundColor Red
    exit 1
}
$dotnetConfig = dotnet test $configTests --nologo -v q 2>&1
$dotnetConfig | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host 'dotnet test (Configuration) FAILED' -ForegroundColor Red
    exit 1
}
$dotnetArch = dotnet test $archTests --nologo -v q 2>&1
$dotnetArch | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host 'dotnet test (Architecture) FAILED' -ForegroundColor Red
    exit 1
}
$dotnetTray = dotnet test $trayTests --nologo -v q 2>&1
$dotnetTray | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host 'dotnet test (Tray) FAILED' -ForegroundColor Red
    exit 1
}
$dotnetLogViewer = dotnet test $logViewerTests --nologo -v q 2>&1
$dotnetLogViewer | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host 'dotnet test (LogViewer) FAILED' -ForegroundColor Red
    exit 1
}
$dotnetSettings = dotnet test $settingsTests --nologo -v q 2>&1
$dotnetSettings | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host 'dotnet test (Settings) FAILED' -ForegroundColor Red
    exit 1
}

$packagingTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.Packaging.Tests\SmartGuard.Packaging.Tests.csproj'
$perfTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.Engine.PerformanceTests\SmartGuard.Engine.PerformanceTests.csproj'

$dotnetPackaging = dotnet test $packagingTests --nologo -v q 2>&1
$dotnetPackaging | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host 'dotnet test (Packaging) FAILED' -ForegroundColor Red
    exit 1
}

$dotnetPerf = dotnet test $perfTests --nologo -v q 2>&1
$dotnetPerf | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host 'dotnet test (Performance) FAILED' -ForegroundColor Red
    exit 1
}

$integrationResult = [pscustomobject]@{ PassedCount = 0; FailedCount = 0 }
$installerFlowResult = [pscustomobject]@{ PassedCount = 0; FailedCount = 0 }
$integration = Join-Path $PSScriptRoot 'Tests\Integration\TrayCoreUserFlow.Tests.ps1'
if (Test-Path -LiteralPath $integration) {
    Write-Host 'Running integration: Tray core user flow...'
    $integrationResult = Invoke-Pester -Path $integration -PassThru
    if ($integrationResult.FailedCount -gt 0) {
        Write-Host "Integration FAILED=$($integrationResult.FailedCount)" -ForegroundColor Red
        exit 1
    }
}
$installerFlow = Join-Path $PSScriptRoot 'Tests\Integration\InstallerUserFlow.Tests.ps1'
if (Test-Path -LiteralPath $installerFlow) {
    if ($env:SMARTGUARD_SKIP_INSTALLER_TESTS -eq '1') {
        Write-Host 'Skipping installer integration: SMARTGUARD_SKIP_INSTALLER_TESTS=1' -ForegroundColor Yellow
    }
    else {
        Write-Host 'Running integration: Installer user flow...'
        $installerFlowResult = Invoke-Pester -Path $installerFlow -PassThru
        if ($installerFlowResult.FailedCount -gt 0) {
            Write-Host "Installer integration FAILED=$($installerFlowResult.FailedCount)" -ForegroundColor Red
            exit 1
        }
    }
}
$total = $r.PassedCount + $integrationResult.PassedCount + $installerFlowResult.PassedCount
$failed = $r.FailedCount + $integrationResult.FailedCount + $installerFlowResult.FailedCount
Write-Host "PASSED=$total FAILED=$failed TOTAL=$($total + $failed)"
if ($failed -gt 0) { exit 1 }
