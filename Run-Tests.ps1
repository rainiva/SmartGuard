$testPath = Join-Path $PSScriptRoot 'Tests\SmartGuard.Tests.ps1'
$resultPath = Join-Path $PSScriptRoot 'test-result.txt'

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

# 预检：确保函数文件可加载
$functionsPath = Join-Path $PSScriptRoot 'lib\SmartGuard.Functions.ps1'
if (-not (Test-Path $functionsPath)) {
    Write-Error "Missing: $functionsPath"
    exit 1
}
. $functionsPath
if (-not (Get-Command Get-ExpectedPlanGuid -ErrorAction SilentlyContinue)) {
    Write-Error "Functions not loaded. Run encoding repair on lib\SmartGuard.Functions.ps1"
    exit 1
}

$r = Invoke-Pester -Path $testPath -PassThru
$engineTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.Engine.Tests\SmartGuard.Engine.Tests.csproj'
$configTests = Join-Path $PSScriptRoot 'Tests\SmartGuard.Configuration.Tests\SmartGuard.Configuration.Tests.csproj'
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
    Write-Host 'Running integration: Installer user flow...'
    $installerFlowResult = Invoke-Pester -Path $installerFlow -PassThru
    if ($installerFlowResult.FailedCount -gt 0) {
        Write-Host "Installer integration FAILED=$($installerFlowResult.FailedCount)" -ForegroundColor Red
        exit 1
    }
}
"$(Get-Date -Format s) PASSED=$($r.PassedCount) FAILED=$($r.FailedCount) TOTAL=$($r.TotalCount)" | Out-File $resultPath -Encoding UTF8
Write-Host "PASSED=$($r.PassedCount) FAILED=$($r.FailedCount) TOTAL=$($r.TotalCount)"
if ($r.FailedCount -gt 0) { exit 1 }
