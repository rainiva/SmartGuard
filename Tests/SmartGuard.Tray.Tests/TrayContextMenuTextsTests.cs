using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayContextMenuTextsTests
{
  [Fact]
  public void Action_menu_includes_immediate_high_performance_switch()
  {
    TrayContextMenuTexts.OrderedActionItems.Should().Contain("立即切换高性能");
    TrayContextMenuTexts.SwitchHighPerformance.Should().Be("立即切换高性能");
  }
}
