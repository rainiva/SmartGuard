using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class TrayPauseArchitectureTests
{
  [Fact]
  public void TrayApplicationContext_must_not_read_config_Paused_for_pause_menu()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayApplicationContext.cs");
    source.Should().NotContain("config.Paused");
    source.Should().NotContain("TryLoad()?.Paused");
    source.Should().Contain("TrayPauseState");
  }
}
