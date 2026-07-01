using System.Text.Json;
using System.Windows.Forms;
using FluentAssertions;
using SmartGuard.Contracts;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayPauseStartupTests
{
  [Fact]
  public void TrayApplicationContext_pause_menu_uses_status_not_config_at_startup()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardTrayPause_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "lib"));
    File.WriteAllText(
      Path.Combine(root, "SmartGuard.config.json"),
      "{\"Paused\":true,\"NotifyOnPlanChange\":true}");
    var statusPath = Path.Combine(root, "SmartGuard.status.json");
    File.WriteAllText(statusPath, JsonSerializer.Serialize(new StatusPayload { paused = false }));

    ToastAumidRegistrar.ResetForTests();
    ToastAumidRegistrar.StartMenuShortcutWriterForTests = _ => true;

    try
    {
      StaTestHost.Run(() =>
      {
        using var context = new TrayApplicationContext(root);
        WaitForPauseItemText(context, "暂停守护", TimeSpan.FromSeconds(2));

        File.WriteAllText(statusPath, JsonSerializer.Serialize(new StatusPayload { paused = true }));
        File.SetLastWriteTimeUtc(statusPath, DateTime.UtcNow.AddSeconds(1));
        WaitForPauseItemText(context, "恢复守护", TimeSpan.FromSeconds(3));
      });
    }
    finally
    {
      ToastAumidRegistrar.ResetForTests();
      try { Directory.Delete(root, true); } catch { }
    }
  }

  private static void WaitForPauseItemText(TrayApplicationContext context, string expected, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      Application.DoEvents();
      Thread.Sleep(50);
      if (GetPauseItemText(context) == expected)
        return;
    }

    GetPauseItemText(context).Should().Be(expected);
  }

  private static string GetPauseItemText(TrayApplicationContext context)
  {
    var field = typeof(TrayApplicationContext).GetField(
      "_pauseItem",
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    field.Should().NotBeNull();
    var item = (ToolStripMenuItem)field!.GetValue(context)!;
    return item.Text;
  }
}
