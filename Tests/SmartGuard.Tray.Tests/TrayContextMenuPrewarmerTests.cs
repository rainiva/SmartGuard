using System.Windows.Forms;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayContextMenuPrewarmerTests
{
    [Fact]
    public void WarmUp_creates_menu_handle_on_ui_thread()
    {
        StaTestHost.Run(() =>
        {
            using var menu = new ContextMenuStrip();
            menu.IsHandleCreated.Should().BeFalse();

            TrayContextMenuPrewarmer.WarmUp(menu);

            menu.IsHandleCreated.Should().BeTrue();
        });
    }
}
