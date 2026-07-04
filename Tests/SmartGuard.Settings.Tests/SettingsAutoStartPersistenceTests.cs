using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsAutoStartPersistenceTests
{
  [Fact]
  public void Auto_start_toggle_reflects_config_AutoStartEnabled_on_open_not_schtasks()
  {
    WpfStaTestHost.Run(() =>
    {
      var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardAutoStart_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempRoot);
      Directory.CreateDirectory(Path.Combine(tempRoot, "bin"));

      SettingsWindowController? controller = null;
      try
      {
        var configPath = SmartGuardPaths.ConfigFile(tempRoot);
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(tempRoot);
        config.AutoStartEnabled = true;
        repository.Save(config);

        controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
        controller.Should().NotBeNull();

        var window = GetWindow(controller!);
        var tglAutoStart = (CheckBox)window.FindName("tglAutoStart")!;
        tglAutoStart.IsChecked.Should().BeTrue(
          "saved config.AutoStartEnabled=true must drive the toggle on reopen, not schtasks SyncFromTasks");
      }
      finally
      {
        controller?.Dispose();
        try { Directory.Delete(tempRoot, true); } catch { }
      }
    });
  }

  [Fact]
  public void Auto_start_toggle_off_when_config_AutoStartEnabled_is_false()
  {
    WpfStaTestHost.Run(() =>
    {
      var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardAutoStartOff_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempRoot);
      Directory.CreateDirectory(Path.Combine(tempRoot, "bin"));

      SettingsWindowController? controller = null;
      try
      {
        var configPath = SmartGuardPaths.ConfigFile(tempRoot);
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(tempRoot);
        config.AutoStartEnabled = false;
        repository.Save(config);

        controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
        controller.Should().NotBeNull();

        var window = GetWindow(controller!);
        var tglAutoStart = (CheckBox)window.FindName("tglAutoStart")!;
        tglAutoStart.IsChecked.Should().BeFalse(
          "config.AutoStartEnabled=false must drive the toggle regardless of schtasks state");
      }
      finally
      {
        controller?.Dispose();
        try { Directory.Delete(tempRoot, true); } catch { }
      }
    });
  }

  private static Window GetWindow(SettingsWindowController controller)
  {
    var field = typeof(SettingsWindowController).GetField(
      "_window",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    return (Window)field!.GetValue(controller)!;
  }
}
