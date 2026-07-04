using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsPauseDisplayTests
{
  [Fact]
  public void Pause_toggle_initial_state_reads_status_json_not_config()
  {
    WpfStaTestHost.Run(() =>
    {
      var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardPauseDisplay_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempRoot);

      SettingsWindowController? controller = null;
      try
      {
        var configPath = SmartGuardPaths.ConfigFile(tempRoot);
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(tempRoot);
        config.Paused = false;
        repository.Save(config);

        var status = new StatusPayload
        {
          currentPlan = "平衡",
          paused = true,
        };
        File.WriteAllText(
          SmartGuardPaths.StatusFile(tempRoot),
          JsonSerializer.Serialize(status));

        controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
        controller.Should().NotBeNull();

        var window = GetWindow(controller!);
        var tglPaused = (CheckBox)window.FindName("tglPaused")!;
        tglPaused.IsChecked.Should().BeTrue("status.json paused=true must win over config.Paused=false");
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
