using SmartGuard.Configuration;
using SmartGuard.Engine.Cli;

namespace SmartGuard.Engine.Tests;

public class InstallCommandsTests
{
  [Fact]
  public void ScheduledTaskNames_delegate_to_registrar()
  {
    InstallPaths.ScheduledTaskNames.Should().BeEquivalentTo(ScheduledTaskRegistrar.TaskNames);
  }

  [Fact]
  public void GetEngineExe_points_to_bin_engine()
  {
    var root = @"D:\Project\SmartGuard";
    InstallPaths.GetEngineExe(root)
      .Should().Be(Path.Combine(root, "bin", "SmartGuard.Engine.exe"));
  }
}
