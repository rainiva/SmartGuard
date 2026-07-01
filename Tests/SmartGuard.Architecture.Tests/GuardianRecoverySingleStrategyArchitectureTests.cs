using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class GuardianRecoverySingleStrategyArchitectureTests
{
  [Fact]
  public void GuardianRecovery_does_not_launch_engine_exe_directly()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardianRecovery.cs");
    source.Should().NotContain("TryLaunchEngine");
    source.Should().NotContain("GetEngineExecutablePath(root)");
    source.Should().Contain("schtasks.exe");
  }
}
