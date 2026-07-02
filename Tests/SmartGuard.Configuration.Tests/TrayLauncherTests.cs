using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class TrayLauncherTests
{
  [Fact]
  public void BuildStartArguments_includes_root()
  {
    var root = @"D:\SmartGuard";
    TrayLauncher.BuildStartArguments(root)
      .Should().Be($"--root \"{root}\"");
  }

  [Fact]
  public void GetTrayExecutablePath_uses_SmartGuardPaths()
  {
    var root = @"D:\SmartGuard";
    TrayLauncher.GetTrayExecutablePath(root)
      .Should().Be(SmartGuardPaths.TrayExe(root));
  }
}
