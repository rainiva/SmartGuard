$testPath = Join-Path $PSScriptRoot 'Tests\SmartPowerPlan.Tests.ps1'
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
$functionsPath = Join-Path $PSScriptRoot 'lib\SmartPowerPlan.Functions.ps1'
if (-not (Test-Path $functionsPath)) {
    Write-Error "Missing: $functionsPath"
    exit 1
}
. $functionsPath
if (-not (Get-Command Get-ExpectedPlanGuid -ErrorAction SilentlyContinue)) {
    Write-Error "Functions not loaded. Run encoding repair on lib\SmartPowerPlan.Functions.ps1"
    exit 1
}

$r = Invoke-Pester -Path $testPath -PassThru
"$(Get-Date -Format s) PASSED=$($r.PassedCount) FAILED=$($r.FailedCount) TOTAL=$($r.TotalCount)" | Out-File $resultPath -Encoding UTF8
Write-Host "PASSED=$($r.PassedCount) FAILED=$($r.FailedCount) TOTAL=$($r.TotalCount)"
if ($r.FailedCount -gt 0) { exit 1 }
