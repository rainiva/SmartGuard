using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public sealed class LogSessionPickerWpfTests
{
  private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

  [Fact]
  public void Session_picker_initializes_to_all_sessions_without_waiting_for_expander()
  {
    WpfStaTestHost.Run(() =>
    {
      var installRoot = CreateInstallRoot();
      SettingsWindowController? controller = null;

      try
      {
        WriteLog(installRoot);
        controller = CreateController(installRoot);
        WpfStaTestHost.Track(controller);
        var window = GetWindow(controller);
        WpfStaTestHost.ShowAndWait(window);

        controller.NavigateTo("logs");
        var combo = RequireCombo(window);

        combo.Items.Count.Should().BeGreaterThan(0);
        combo.SelectedIndex.Should().Be(0);
        combo.SelectedItem.Should().BeEquivalentTo(new
        {
          Kind = LogSessionPickerItemKind.All,
          Title = "全部会话",
          IsSelectable = true,
        });
      }
      finally
      {
        controller?.Dispose();
        TryDelete(installRoot);
      }
    });
  }

  [Fact]
  public void Session_picker_loads_sessions_from_dropdown_and_keeps_all_item()
  {
    WpfStaTestHost.Run(() =>
    {
      var installRoot = CreateInstallRoot();
      SettingsWindowController? controller = null;

      try
      {
        WriteLog(installRoot);
        controller = CreateController(installRoot);
        WpfStaTestHost.Track(controller);
        var window = GetWindow(controller);
        WpfStaTestHost.ShowAndWait(window);

        controller.NavigateTo("logs");
        var combo = RequireCombo(window);
        var host = GetLogPageHost(controller);
        InvokePrivate(host, "OnSessionComboDropDownOpened", combo, EventArgs.Empty);

        WpfStaTestHost.WaitUntil(
          () => combo.Items.OfType<LogSessionPickerItem>().Any(item => item.Kind == LogSessionPickerItemKind.Session),
          TimeSpan.FromSeconds(10),
          "session metadata to load from ComboBox.DropDownOpened");

        combo.Items.OfType<LogSessionPickerItem>().First().Kind.Should().Be(LogSessionPickerItemKind.All);
        combo.Items.OfType<LogSessionPickerItem>().Should().Contain(item => item.Kind == LogSessionPickerItemKind.Session);
      }
      finally
      {
        controller?.Dispose();
        TryDelete(installRoot);
      }
    });
  }

  [Fact]
  public void Session_picker_lives_in_top_scope_row_renders_session_titles_and_wraps_at_min_width()
  {
    WpfStaTestHost.Run(() =>
    {
      var installRoot = CreateInstallRoot();
      SettingsWindowController? controller = null;

      try
      {
        WriteLog(installRoot);
        controller = CreateController(installRoot);
        WpfStaTestHost.Track(controller);
        var window = GetWindow(controller);
        window.Width = 600;
        window.Height = 500;
        WpfStaTestHost.ShowAndWait(window);

        controller.NavigateTo("logs");
        var combo = RequireCombo(window);
        var scopePanel = window.FindName("logFilterScopePanel") as WrapPanel
          ?? throw new InvalidOperationException("logFilterScopePanel was not found.");
        combo.Parent.Should().BeSameAs(scopePanel);

        var host = GetLogPageHost(controller);
        InvokePrivate(host, "OnSessionComboDropDownOpened", combo, EventArgs.Empty);

        WpfStaTestHost.WaitUntil(
          () => combo.Items.OfType<LogSessionPickerItem>().Any(item => item.Kind == LogSessionPickerItemKind.Session),
          TimeSpan.FromSeconds(10),
          "session metadata to load from ComboBox.DropDownOpened");

        var sessionItem = combo.Items.OfType<LogSessionPickerItem>()
          .First(item => item.Kind == LogSessionPickerItemKind.Session);
        sessionItem.Title.Should().NotBeNullOrWhiteSpace();
        combo.SelectedItem = sessionItem;
        window.UpdateLayout();

        combo.ActualWidth.Should().BeGreaterThan(0);
        scopePanel.ActualWidth.Should().BeGreaterThan(0);
        var scopeBounds = scopePanel.TransformToAncestor(window)
          .TransformBounds(new Rect(new Point(0, 0), scopePanel.RenderSize));
        scopeBounds.Left.Should().BeGreaterThanOrEqualTo(0);
        scopeBounds.Right.Should().BeLessThanOrEqualTo(window.ActualWidth + 1);

        var sessionBounds = combo.TransformToAncestor(window)
          .TransformBounds(new Rect(new Point(0, 0), combo.RenderSize));
        sessionBounds.Width.Should().BeGreaterThan(0);
        sessionBounds.Left.Should().BeGreaterThanOrEqualTo(0);
        sessionBounds.Right.Should().BeLessThanOrEqualTo(window.ActualWidth + 1);
      }
      finally
      {
        controller?.Dispose();
        TryDelete(installRoot);
      }
    });
  }

  [Fact]
  public void Statistics_source_uses_independent_inspector_dirty_flag_and_generation_cancellation()
  {
    var source = File.ReadAllText(
      Path.Combine(SettingsSourcePaths.RepoRoot(), "src", "SmartGuard.Settings", "SettingsLogPageHost.Statistics.cs"),
      Utf8NoBom);

    source.Should().Contain("_logStatisticsInspector");
    source.Should().Contain("_statisticsDirty");
    source.Should().Contain("_logStatisticsInspector.Expanded");
    source.Should().Contain("_logStatisticsInspector.IsExpanded");
    source.Should().Contain("_statisticsCancellation?.Cancel()");
    source.Should().Contain("generation != _statisticsGeneration");
    source.Should().NotContain("_logSessionInspector.Expanded");
  }

  private static SettingsWindowController CreateController(string installRoot)
  {
    var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
    File.WriteAllText(
      configPath,
      "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000,\"ThemeFollowSystem\":false,\"ThemeIsDark\":false}",
      Utf8NoBom);
    var repository = new GuardConfigRepository(configPath);
    var config = repository.LoadOrDefault(installRoot);
    return SettingsWindowController.TryCreate(installRoot, repository, config)
      ?? throw new InvalidOperationException("Failed to create SettingsWindowController for session picker WPF test.");
  }

  private static string CreateInstallRoot()
  {
    var installRoot = Path.Combine(Path.GetTempPath(), "sg-log-session-picker-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(installRoot);
    Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
    return installRoot;
  }

  private static void WriteLog(string installRoot)
  {
    File.WriteAllText(
      Path.Combine(installRoot, "SmartGuard.log"),
      "[INFO] 2026-08-02 10:00:00 [Engine] AppStarted\n[INFO] 2026-08-02 10:01:00 [Engine] session body\n[INFO] 2026-08-02 10:02:00 [Engine] AppStopping\n",
      Utf8NoBom);
  }

  private static ComboBox RequireCombo(Window window)
    => window.FindName("cmbLogSession") as ComboBox
       ?? throw new InvalidOperationException("cmbLogSession was not found.");

  private static Window GetWindow(SettingsWindowController controller)
  {
    var field = typeof(SettingsWindowController).GetField(
      "_window",
      BindingFlags.NonPublic | BindingFlags.Instance);
    return (Window)field!.GetValue(controller)!;
  }

  private static object GetLogPageHost(SettingsWindowController controller)
  {
    var field = typeof(SettingsWindowController).GetField(
      "_logPageHost",
      BindingFlags.NonPublic | BindingFlags.Instance);
    return field!.GetValue(controller)!;
  }

  private static void InvokePrivate(object instance, string methodName, params object[] args)
  {
    var method = instance.GetType().GetMethod(
      methodName,
      BindingFlags.NonPublic | BindingFlags.Instance);
    method.Should().NotBeNull();
    method!.Invoke(instance, args);
  }

  private static void TryDelete(string path)
  {
    try { Directory.Delete(path, recursive: true); } catch { }
  }
}
