using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class RunTestsOptimizationArchitectureTests
{
  [Fact]
  public void Run_Tests_must_delegate_dotnet_tests_to_parallel_helper()
  {
    var runTests = SourceScanHelper.ReadSource("Run-Tests.ps1");
    runTests.Should().Contain("Invoke-SmartGuardDotNetTests.ps1");
    runTests.Should().Contain("Invoke-SmartGuardDotNetTestSuite");
    runTests.Should().NotMatchRegex(@"dotnet test \$engineTests");
  }

  [Fact]
  public void Dotnet_test_helper_must_build_once_and_poll_parallel_processes_with_no_build()
  {
    var helper = SourceScanHelper.ReadSource("scripts/Invoke-SmartGuardDotNetTests.ps1");
    helper.Should().Contain("dotnet-test-$RunId.stdout.log");
    helper.Should().Contain("function Start-SmartGuardCapturedCommand");
    helper.Should().Contain("function Complete-SmartGuardCapturedCommand");
    helper.Should().Contain("function Read-SmartGuardTrxResult");
    helper.Should().Contain("$Entry.Process.ExitCode");
    helper.Should().Contain("trx;LogFileName=$trxFileName");
    helper.Should().Contain("$counterValues.total -le 0");
    helper.Should().NotContain(".exit");
    helper.Should().NotContain("ExitPath");
    helper.Should().NotContain("%ERRORLEVEL%");
    var capturedCommandProtocol = System.Text.RegularExpressions.Regex.Match(
      helper,
      @"(?s)function Start-SmartGuardCapturedCommand.*?(?=\r?\nfunction Read-SmartGuardTrxResult)");
    capturedCommandProtocol.Success.Should().BeTrue();
    capturedCommandProtocol.Value.Should().NotContain("$LASTEXITCODE");
    helper.Should().Contain("function Read-SmartGuardTextFileWhenUnlocked");
    helper.Should().Contain("catch [System.IO.IOException]");
    helper.Should().Contain("Stop-Process");
    helper.Should().NotContain("Wait-Job");
    helper.Should().Contain("foreach ($project in $serialProjects)");
    helper.Should().Contain("Get-SmartGuardParallelDotNetTestProjectPaths");
    helper.Should().Contain("Get-SmartGuardSerialDotNetTestProjectPaths");
    helper.Should().Contain("--no-restore");
    helper.Should().Contain("--no-build");
    helper.Should().Contain("Ensure-SmartGuardDotNetTestBuild");

    var runTests = SourceScanHelper.ReadSource("Run-Tests.ps1");
    runTests.Should().Contain("$dotNetResult.Result -ne 'Passed'");
    runTests.Should().Contain("run-tests-$runId.summary.json");
    runTests.Should().Contain("$summaryJson = $summary | ConvertTo-Json -Depth 10");
    runTests.Should().MatchRegex(
      @"\[System\.IO\.File\]::WriteAllText\(\s*\$summaryPath,\s*\$summaryJson,\s*\[System\.Text\.UTF8Encoding\]::new\(\$false\)\s*\)");
  }

  [Theory]
  [InlineData("fast", "Category!=Performance&Category!=Live&Category!=WpfUi")]
  [InlineData("full", "Category!=Performance&Category!=Live")]
  [InlineData("nightly", null)]
  public void Dotnet_test_helper_maps_tiers_to_filters(string tier, string? expectedFilter)
  {
    var helper = SourceScanHelper.ReadSource("scripts/Invoke-SmartGuardDotNetTests.ps1");
    helper.Should().Contain($"'{tier}'");
    if (expectedFilter is null)
      helper.Should().Contain("'nightly' { return $null");
    else
      helper.Should().Contain(expectedFilter);
  }

  [Fact]
  public void Performance_tests_must_be_category_performance()
  {
    var source = SourceScanHelper.ReadSource(
      "Tests/SmartGuard.Engine.PerformanceTests/EnginePerformanceTests.cs");
    source.Should().Contain("[Trait(\"Category\", \"Performance\")]");
  }

  [Fact]
  public void Live_catalog_probe_must_be_category_live()
  {
    var source = SourceScanHelper.ReadSource(
      "Tests/SmartGuard.Configuration.Tests/LiveCatalogProbeTests.cs");
    source.Should().Contain("[Trait(\"Category\", \"Live\")]");
  }

  [Fact]
  public void Wpf_ui_tests_must_expose_category_trait()
  {
    var source = SourceScanHelper.ReadSource(
      "Tests/SmartGuard.Settings.Tests/SettingsColdStartTests.cs");
    source.Should().Contain("[Collection(\"WpfUiTests\")]");
    source.Should().Contain("[Trait(\"Category\", \"WpfUi\")]");
  }

  [Fact]
  public void Tray_core_user_flow_must_not_invoke_nested_dotnet_test()
  {
    var source = SourceScanHelper.ReadSource("Tests/Integration/TrayCoreUserFlow.Tests.ps1");
    source.Should().NotContain("dotnet test");
  }

  [Fact]
  public void SmartGuardStop_must_poll_for_process_exit_instead_of_fixed_two_second_sleep()
  {
    var source = SourceScanHelper.ReadSource("Tests/Integration/SmartGuardStop.ps1");
    source.Should().Contain("Wait-SmartGuardEngineExit");
    source.Should().NotMatchRegex(@"Start-Sleep\s+-Seconds\s+2");
  }

  [Fact]
  public void Run_Tests_must_not_elevate_the_whole_suite()
  {
    var runTests = SourceScanHelper.ReadSource("Run-Tests.ps1");
    runTests.Should().NotContain("Test-RunTestsNeedsInstallerElevation");
    runTests.Should().NotContain("Invoke-RunTestsSingleElevation");
    runTests.Should().Contain("$installerFlowResult = Invoke-Pester -Path $installerFlow -PassThru");
  }

  [Fact]
  public void SmartGuardStop_must_avoid_uninstall_uac_when_not_elevated()
  {
    var source = SourceScanHelper.ReadSource("Tests/Integration/SmartGuardStop.ps1");
    source.Should().Contain("Stop-SmartGuardProcessesWithoutElevation");
    source.Should().MatchRegex(@"if\s*\(\s*-not\s*\(\s*Test-IsProcessElevated\s*\)\s*\)");
  }

  [Fact]
  public void Installer_integration_gate_must_allow_the_flow_to_own_its_single_elevation()
  {
    var helper = SourceScanHelper.ReadSource("scripts/Invoke-SmartGuardDotNetTests.ps1");
    var fnMatch = System.Text.RegularExpressions.Regex.Match(
      helper,
      @"(?s)function Test-SmartGuardInstallerIntegrationEnabled.*?(?=\r?\nfunction |\z)");
    fnMatch.Success.Should().BeTrue();
    fnMatch.Value.Should().NotContain("Test-IsProcessElevated");
    fnMatch.Value.Should().NotMatchRegex(
      @"if\s*\(\s*\$Tier\s*-eq\s*'nightly'\s*\)\s*\{\s*return\s+\$true\s*\}");
  }

  [Fact]
  public void Invoke_InstallerUserFlowSingleElevation_must_use_explicit_windows_powershell_and_surface_launch_failures()
  {
    var elevation = SourceScanHelper.ReadSource("scripts/Test-IsProcessElevated.ps1");
    var fnMatch = System.Text.RegularExpressions.Regex.Match(
      elevation,
      @"(?s)function Invoke-InstallerUserFlowSingleElevation.*?(?=\r?\nfunction |\z)");
    fnMatch.Success.Should().BeTrue();
    elevation.Should().Contain("function Get-WindowsPowerShellExePath");
    fnMatch.Value.Should().Contain("$powershellExe = Get-WindowsPowerShellExePath");
    fnMatch.Value.Should().Contain("Start-Process -FilePath $powershellExe -Verb RunAs");
    fnMatch.Value.Should().Contain("Unable to launch elevated Windows PowerShell");
  }
}
