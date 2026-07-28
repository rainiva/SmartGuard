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

    var loop = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
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
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    source.Should().Contain("ResolveLogFile");
  }

  [Fact]
  public void GuardianLoop_plan_probe_must_be_fault_tolerant()
  {
    // A bare PowerCfgExecutor.GetCurrentPlanGuid() call in the main loop let a
    // transient powercfg timeout crash the whole engine (0xE0434352); the probe
    // must go through GuardianPlanProbe so failures degrade instead of killing.
    var loop = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    loop.Should().Contain("GuardianPlanProbe.ProbeOrKeep");
    loop.Should().NotContain("= PowerCfgExecutor.GetCurrentPlanGuid()");
  }
}
