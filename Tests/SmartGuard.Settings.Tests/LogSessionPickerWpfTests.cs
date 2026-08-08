using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

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
  public void Active_session_metadata_reload_updates_active_scope_without_overwriting_main_view_snapshot()
  {
    WpfStaTestHost.Run(() =>
    {
      var installRoot = CreateInstallRoot();
      SettingsWindowController? controller = null;

      try
      {
        WriteActiveLog(installRoot);
        controller = CreateController(installRoot);
        WpfStaTestHost.Track(controller);
        var window = GetWindow(controller);
        WpfStaTestHost.ShowAndWait(window);

        controller.NavigateTo("logs");
        var combo = RequireCombo(window);
        var host = (SettingsLogPageHost)GetLogPageHost(controller);
        var searchBar = GetPrivateField<LogSearchFilterBar>(host, "_logSearchFilterBar");
        var logController = GetPrivateField<LogViewController>(host, "_logController");

        InvokePrivate(host, "OnSessionComboDropDownOpened", combo, EventArgs.Empty);
        WpfStaTestHost.WaitUntil(
          () => combo.Items.OfType<LogSessionPickerItem>().Any(item => item.Kind == LogSessionPickerItemKind.Session),
          TimeSpan.FromSeconds(10),
          "initial active session metadata to load");

        var activeItem = combo.Items.OfType<LogSessionPickerItem>()
          .Single(item => item.Kind == LogSessionPickerItemKind.Session);
        activeItem.Session!.IsActive.Should().BeTrue("the fixture starts before an AppStopping event");

        searchBar.SetKeyword("main-before");
        combo.SelectedItem = activeItem;
        WpfStaTestHost.WaitUntil(
          () => host.SessionCoordinatorForTests.ActiveSession?.SessionId == activeItem.Session.SessionId,
          TimeSpan.FromSeconds(10),
          "session selection to become active");

        searchBar.SetKeyword("inside-session");
        var end = new DateTimeOffset(2026, 8, 2, 10, 2, 0, activeItem.Session.StartTimestamp.Offset);
        AppendStopForActiveLog(installRoot);

        SetPrivateField(host, "_sessionMetadataLoaded", false);
        InvokePrivate(host, "OnSessionComboDropDownOpened", combo, EventArgs.Empty);
        WpfStaTestHost.WaitUntil(
          () => combo.SelectedItem is LogSessionPickerItem { Session.IsActive: false },
          TimeSpan.FromSeconds(10),
          "reloaded metadata for the same active session id to close the active session");

        host.SessionCoordinatorForTests.ActiveSession.Should().NotBeNull();
        host.SessionCoordinatorForTests.ActiveSession!.EndTimestamp.Should().Be(end);
        logController.ScopeExpression.Should().NotBeNull();
        LogQueryExpressionEvaluator.Matches(
          CreateEvent("after-updated-session", end.AddMinutes(1)),
          logController.ScopeExpression!).Should().BeFalse(
            "the active controller scope should use the reloaded session end boundary");

        combo.SelectedIndex = 0;
        WpfStaTestHost.WaitUntil(
          () => !host.SessionCoordinatorForTests.IsActive,
          TimeSpan.FromSeconds(10),
          "session view to exit through the standard All selection");
        searchBar.Keyword.Should().Be("main-before",
          "reloading metadata for the active SessionId must not overwrite the first main-view snapshot");
      }
      finally
      {
        controller?.Dispose();
        TryDelete(installRoot);
      }
    });
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

  private static void WriteActiveLog(string installRoot)
  {
    File.WriteAllText(
      Path.Combine(installRoot, "SmartGuard.log"),
      "[INFO] 2026-08-02 10:00:00 [Engine] AppStarted\n[INFO] 2026-08-02 10:01:00 [Engine] session body\n",
      Utf8NoBom);
  }

  private static void AppendStopForActiveLog(string installRoot)
  {
    File.AppendAllText(
      Path.Combine(installRoot, "SmartGuard.log"),
      "[INFO] 2026-08-02 10:02:00 [Engine] AppStopping\n",
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

  private static LogEvent CreateEvent(string eventId, DateTimeOffset timestamp)
    => new(
      LogEvent.CurrentSchemaVersion,
      eventId,
      timestamp,
      LogSeverity.Info,
      "Engine",
      "Runtime",
      "TestEvent",
      "test event",
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      LogEvent.EmptyProperties);

  private static object GetLogPageHost(SettingsWindowController controller)
  {
    var field = typeof(SettingsWindowController).GetField(
      "_logPageHost",
      BindingFlags.NonPublic | BindingFlags.Instance);
    return field!.GetValue(controller)!;
  }

  private static T GetPrivateField<T>(object instance, string fieldName)
  {
    var field = instance.GetType().GetField(
      fieldName,
      BindingFlags.NonPublic | BindingFlags.Instance);
    field.Should().NotBeNull();
    return (T)field!.GetValue(instance)!;
  }

  private static void SetPrivateField<T>(object instance, string fieldName, T value)
  {
    var field = instance.GetType().GetField(
      fieldName,
      BindingFlags.NonPublic | BindingFlags.Instance);
    field.Should().NotBeNull();
    field!.SetValue(instance, value);
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
