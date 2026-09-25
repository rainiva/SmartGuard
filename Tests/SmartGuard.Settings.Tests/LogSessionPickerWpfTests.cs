using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
  public void Filter_popup_exposes_level_and_source_dropdowns_while_session_picker_stays_hidden()
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
        var filterPanel = window.FindName("logFilterPanel") as Border
          ?? throw new InvalidOperationException("logFilterPanel was not found.");
        var filterPopup = window.FindName("logFilterPopup") as Popup
          ?? throw new InvalidOperationException("logFilterPopup was not found.");
        filterPopup.IsOpen.Should().BeFalse();
        filterPanel.Visibility.Should().Be(Visibility.Visible);
        var filterButton = window.FindName("btnLogFilter") as Button
          ?? throw new InvalidOperationException("btnLogFilter was not found.");
        filterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        filterPopup.IsOpen.Should().BeTrue();

        var levelCombo = window.FindName("cmbLogLevel") as ComboBox
          ?? throw new InvalidOperationException("cmbLogLevel was not found.");
        var sourceCombo = window.FindName("cmbLogSource") as ComboBox
          ?? throw new InvalidOperationException("cmbLogSource was not found.");
        var sessionCombo = RequireCombo(window);
        var toolbar = window.FindName("logToolbar") as Grid
          ?? throw new InvalidOperationException("logToolbar was not found.");

        levelCombo.Visibility.Should().Be(Visibility.Visible);
        sourceCombo.Visibility.Should().Be(Visibility.Visible);
        sessionCombo.Visibility.Should().Be(Visibility.Collapsed);
        levelCombo.Items.OfType<ComboBoxItem>().Should().Contain(item => Equals(item.Content, "全部级别"));
        sourceCombo.Items.OfType<ComboBoxItem>().Should().Contain(item => Equals(item.Content, "全部来源"));
        sourceCombo.Items.OfType<ComboBoxItem>().Should().Contain(item => Equals(item.Tag, LogComponentRegistry.Engine));
        filterPopup.PlacementTarget.Should().BeSameAs(filterButton);
        filterPanel.ActualWidth.Should().BeLessThanOrEqualTo(toolbar.ActualWidth + 0.5);
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
        var paginationCoordinator = host.PaginationCoordinatorForTests
          ?? throw new InvalidOperationException("The log page must create its pagination coordinator.");

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
        paginationCoordinator.State.IsStale.Should().BeTrue(
          "entering a session must invalidate the current main-log snapshot");

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

  [Fact]
  public void Paged_query_retries_snapshot_invalidation_once_then_surfaces_error()
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

        var host = (SettingsLogPageHost)GetLogPageHost(controller);
        WpfStaTestHost.WaitUntil(
          () => host.IsLogViewInitializedForTests && !controller.IsLogRefreshInFlightForTests,
          TimeSpan.FromSeconds(10),
          "the log host to initialize and settle before replacing its controller");
        host.SetLogPageActive(false);

        var source = new InvalidationSnapshotSource();
        SetPrivateField(
          host,
          "_logController",
          new LogViewController(source, Path.Combine(installRoot, "SmartGuard.log")));

        var expression = LogQueryParser.Parse("message~event").Expression!;
        var intent = new LogPaginationIntent(
          QueryGeneration: 7,
          expression,
          new LogReadScope(Array.Empty<LogSourceIdentity>()),
          PageNumber: 1,
          PageSize: 20,
          LogSortDirection.NewestFirst,
          IgnoreCase: true,
          CaptureNewSnapshot: true);
        var models = new List<LogPaginationApplyModel>();
        var progress = new InlineProgress<LogPaginationApplyModel>(models.Add);
        var execute = typeof(SettingsLogPageHost).GetMethod(
          "ExecutePagedQueryAsync",
          BindingFlags.NonPublic | BindingFlags.Instance);
        execute.Should().NotBeNull();

        ((Task)execute!.Invoke(
          host,
          [intent, null, null, progress, CancellationToken.None])!).GetAwaiter().GetResult();

        source.CaptureSnapshotCalls.Should().Be(2,
          "the executor may capture a new snapshot only for the initial attempt and one retry");
        source.SnapshotEnumerationCalls.Should().Be(2,
          "the second invalidation must be surfaced rather than retried indefinitely");
        models.Should().ContainSingle();
        models[0].QueryGeneration.Should().Be(intent.QueryGeneration);
        models[0].IsProvisional.Should().BeFalse();
        models[0].ExactTotalCount.Should().BeNull();
        models[0].ErrorMessage.Should().NotBeNullOrWhiteSpace();
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
    => CreateEvent(eventId, timestamp, "Engine", "test event");

  private static LogEvent CreateEvent(
    string eventId,
    DateTimeOffset timestamp,
    string component,
    string message)
    => new(
      LogEvent.CurrentSchemaVersion,
      eventId,
      timestamp,
      LogSeverity.Info,
      component,
      "Runtime",
      "TestEvent",
      message,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      LogEvent.EmptyProperties);

  private static LogSessionMetadata CreateSession(
    string sessionId,
    DateTimeOffset startTimestamp,
    DateTimeOffset? endTimestamp)
    => new(
      sessionId,
      startTimestamp,
      endTimestamp,
      ProcessId: 42,
      Component: "Engine",
      IsActive: endTimestamp is null,
      StartEventId: sessionId + "-start",
      EndEventId: endTimestamp is null ? null : sessionId + "-stop");

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

  private static void SetPrivateProperty<T>(object instance, string propertyName, T value)
  {
    var property = instance.GetType().GetProperty(
      propertyName,
      BindingFlags.NonPublic | BindingFlags.Instance);
    property.Should().NotBeNull();
    property!.SetValue(instance, value);
  }

  private static void InvokePrivate(object instance, string methodName, params object[] args)
  {
    var method = instance.GetType().GetMethod(
      methodName,
      BindingFlags.NonPublic | BindingFlags.Instance);
    method.Should().NotBeNull();
    method!.Invoke(instance, args);
  }

  private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
  {
    public void Report(T value) => handler(value);
  }

  private sealed class InvalidationSnapshotSource : ILogEventSource, ILogSnapshotPhysicalLineSource
  {
    private readonly LogSourceIdentity _source = LogSourceIdentity.FromContentPrefix(
      new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero),
      "task6-retry-source");
    private readonly LogPhysicalLine _line;

    internal InvalidationSnapshotSource()
    {
      _line = new LogPhysicalLine(
        _source,
        ByteOffset: 0,
        "[INFO] 2026-08-02 10:00:00 [Engine] retry event",
        IsComplete: true);
    }

    internal int CaptureSnapshotCalls { get; private set; }

    internal int SnapshotEnumerationCalls { get; private set; }

    public Task<LogEventPage> ReadLatestAsync(
      LogReadRequest request,
      CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadOlderAsync(
      LogReadRequest request,
      CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadNewerAsync(
      LogReadRequest request,
      CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public IAsyncEnumerable<LogEvent> StreamAsync(
      LogReadScope scope,
      CancellationToken cancellationToken)
      => throw new InvalidOperationException("The unbounded event stream must not be used.");

    public IEnumerable<LogPhysicalLine> EnumeratePhysicalLines(
      LogReadScope scope,
      CancellationToken cancellationToken)
      => throw new InvalidOperationException("The unbounded physical-line stream must not be used.");

    public IAsyncEnumerable<LogPhysicalLine> StreamPhysicalLinesAsync(
      LogReadScope scope,
      CancellationToken cancellationToken)
      => throw new InvalidOperationException("The unbounded physical-line stream must not be used.");

    public LogSourceSnapshot CaptureSnapshot()
    {
      CaptureSnapshotCalls++;
      return new LogSourceSnapshot(
        [new LogSourceSnapshotEntry(
          _source,
          LogSourceKind.Current,
          CapturedLength: 256,
          BoundaryFingerprint: "task6-retry-boundary")],
        Fingerprint: "task6-retry-snapshot");
    }

    public IEnumerable<LogPhysicalLine> EnumeratePhysicalLines(
      LogReadScope scope,
      LogSourceSnapshot snapshot,
      LogReadDirection direction,
      LogReadCursor? cursor,
      CancellationToken cancellationToken)
    {
      SnapshotEnumerationCalls++;
      if (SnapshotEnumerationCalls <= 2)
        throw new LogSnapshotInvalidatedException("task6 test snapshot invalidated");

      return [_line];
    }
  }

  private static void TryDelete(string path)
  {
    try { Directory.Delete(path, recursive: true); } catch { }
  }
}
