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
  public void Dotnet_test_helper_must_build_once_and_run_parallel_with_no_build()
  {
    var helper = SourceScanHelper.ReadSource("scripts/Invoke-SmartGuardDotNetTests.ps1");
    helper.Should().Contain("Start-Job");
    helper.Should().Contain("Get-SmartGuardParallelDotNetTestProjectPaths");
    helper.Should().Contain("Get-SmartGuardSerialDotNetTestProjectPaths");
    helper.Should().Contain("--no-build");
    helper.Should().Contain("Ensure-SmartGuardDotNetTestBuild");
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
}
