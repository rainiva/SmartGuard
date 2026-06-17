namespace SmartGuard.Tray.Tests;

public class SmartGuardToastAppIdTests
{
  [Fact]
  public void AppId_matches_ps_contract()
  {
    SmartGuardToastAppId.AppId.Should().Be("Tools.SmartGuard.Guardian");
  }

  [Fact]
  public void DisplayName_is_smart_guard()
  {
    SmartGuardToastAppId.DisplayName.Should().Be("智能电源守护");
  }
}
