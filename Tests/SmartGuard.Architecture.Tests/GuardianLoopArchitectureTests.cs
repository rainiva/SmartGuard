using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class GuardianLoopArchitectureTests
{
  [Fact]
  public void GuardianLoop_must_delegate_first_run_and_exception_handling()
  {
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Engine",
        "Worker",
        "GuardianFirstRunInitializer.cs"))
      .Should().BeTrue();
    File.Exists(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Engine",
        "Worker",
        "GuardianExceptionStormHandler.cs"))
      .Should().BeTrue();

    var loop = SourceScanHelper.ReadGuardianLoopSource();
    loop.Should().Contain("GuardianFirstRunInitializer");
    loop.Should().Contain("GuardianExceptionStormHandler");
    loop.Should().NotContain("INIT: 开始首次初始化");
    loop.Should().NotContain("同类异常");

    File.ReadAllLines(Path.Combine(
        SourceScanHelper.RepoRoot,
        "src",
        "SmartGuard.Engine",
        "Worker",
        "GuardianLoop.cs")).Length.Should().BeLessThan(300);
  }

  [Fact]
  public void GuardianLoop_WriteLog_uses_ResolveLogFile()
  {
    var source = SourceScanHelper.ReadGuardianLoopSource();
    source.Should().Contain("ResolveLogFile");
  }

  [Fact]
  public void GuardianLoop_plan_probe_must_be_result_driven_and_fail_closed()
  {
    var loop = SourceScanHelper.ReadGuardianLoopSource();
    var runner = SourceScanHelper.ReadSource(
      "src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs");
    loop.Should().NotContain("GuardianPlanProbe");
    loop.Should().NotContain("= PowerCfgExecutor.GetCurrentPlanGuid()");
    runner.Should().Contain("PowerPlanProbeResult");
    runner.Should().Contain("PowerPlanApplyFailed");
  }
}
