using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class TrayLifecycleTests
{
  [Fact]
  public void TrayProcessImageName_matches_paths_single_source()
  {
    TrayLifecycle.TrayProcessImageName
      .Should().Be(SmartGuardPaths.TrayExeFileName);
  }
}
