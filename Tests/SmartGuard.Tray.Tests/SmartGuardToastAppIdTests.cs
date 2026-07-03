using SmartGuard.Configuration.Shell;

namespace SmartGuard.Tray.Tests;

public class SmartGuardToastAppIdTests
{
  [Fact]
  public void AppId_matches_ps_contract()
  {
    SmartGuardToastAppId.AppId.Should().Be("Tools.SmartGuard.Guardian");
  }

  [Fact]
  public void DisplayName_is_non_empty()
  {
    SmartGuardToastAppId.DisplayName.Should().NotBeNullOrWhiteSpace();
  }
}
