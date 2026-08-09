using System.Reflection;
using System.Runtime.CompilerServices;
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
  public void Session_picker_lives_in_filter_panel_renders_session_titles_and_wraps_at_min_width()
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
        filterPanel.Visibility.Should().Be(Visibility.Collapsed);
        var filterButton = window.FindName("btnLogFilter") as Button
          ?? throw new InvalidOperationException("btnLogFilter was not found.");
        filterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        filterPanel.Visibility.Should().Be(Visibility.Visible);

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

        var paginationCoordinator = ((SettingsLogPageHost)GetLogPageHost(controller)).PaginationCoordinatorForTests
          ?? throw new InvalidOperationException("The log page must create its pagination coordinator.");
        WpfStaTestHost.WaitUntil(
          () => paginationCoordinator.State.IsScanComplete,
          TimeSpan.FromSeconds(10),
          "session selection to rebuild the exact paged query");
        paginationCoordinator.State.ExactTotalCount.Should().NotBeNull();
        paginationCoordinator.State.TotalPages.Should().BeGreaterThanOrEqualTo(1);

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
          () => host.IsLogViewInitializedForTests,
          TimeSpan.FromSeconds(10),
          "the log host to initialize before invoking the paged executor");
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

  [Fact]
  public void Statistics_inspector_defers_until_expanded_and_filter_refresh_applies_latest_generation()
  {
    WpfStaTestHost.Run(() =>
    {
      var installRoot = CreateInstallRoot();
      SettingsWindowController? controller = null;
      ControllableAggregationEventSource? source = null;

      try
      {
        WriteLog(installRoot);
        controller = CreateController(installRoot);
        WpfStaTestHost.Track(controller);
        var window = GetWindow(controller);
        WpfStaTestHost.ShowAndWait(window);

        controller.NavigateTo("diagnostic");
        var host = (SettingsLogPageHost)GetLogPageHost(controller);
        source = AttachControllableAggregationSource(host, installRoot);
        var searchBar = GetPrivateField<LogSearchFilterBar>(host, "_logSearchFilterBar");
        var statisticsInspector = RequireStatisticsInspector(window);
        var componentDistribution = RequireComponentDistribution(window);

        InvokePrivate(host, "OnFilterRefresh", false);

        source.FlightCount.Should().Be(0, "collapsed statistics must only mark dirty");
        host.AggregationServiceForTests.CurrentGeneration.Should().Be(0);

        statisticsInspector.IsExpanded = true;
        WpfStaTestHost.WaitUntil(
          () => source.FlightCount == 1,
          TimeSpan.FromSeconds(10),
          "first statistics generation to start after expanding the inspector");
        host.AggregationServiceForTests.CurrentGeneration.Should().Be(1);

        searchBar.SetKeyword("filter-latest");
        InvokePrivate(host, "OnFilterRefresh", false);

        WpfStaTestHost.WaitUntil(
          () => source.FlightCount == 2,
          TimeSpan.FromSeconds(10),
          "filter refresh to start a superseding statistics generation");
        WpfStaTestHost.WaitUntil(
          () => source.FlightAt(0).WasCancelled,
          TimeSpan.FromSeconds(10),
          "first statistics generation to be cancelled by filter refresh");
        host.AggregationServiceForTests.CurrentGeneration.Should().Be(2);

        source.ReleaseFlight(
          1,
          CreateEvent("filter-latest-event", new DateTimeOffset(2026, 8, 2, 10, 1, 0, TimeSpan.FromHours(8)), "FilterLatest", "filter-latest"));
        WpfStaTestHost.WaitUntil(
          () => ComponentNames(componentDistribution).Contains("FilterLatest"),
          TimeSpan.FromSeconds(10),
          "latest filter statistics result to render");

        source.ReleaseFlight(
          0,
          CreateEvent("stale-filter-event", new DateTimeOffset(2026, 8, 2, 10, 1, 0, TimeSpan.FromHours(8)), "StaleFilter", "filter-latest"));
        WpfStaTestHost.PumpDispatcher(window);

        ComponentNames(componentDistribution).Should().Contain("FilterLatest");
        ComponentNames(componentDistribution).Should().NotContain("StaleFilter");
      }
      finally
      {
        source?.ReleaseAll();
        controller?.Dispose();
        TryDelete(installRoot);
      }
    });
  }

  [Fact]
  public void Statistics_inspector_session_changes_refresh_scoped_generation_and_supersede_stale_results()
  {
    WpfStaTestHost.Run(() =>
    {
      var installRoot = CreateInstallRoot();
      SettingsWindowController? controller = null;
      ControllableAggregationEventSource? source = null;

      try
      {
        WriteLog(installRoot);
        controller = CreateController(installRoot);
        WpfStaTestHost.Track(controller);
        var window = GetWindow(controller);
        WpfStaTestHost.ShowAndWait(window);

        controller.NavigateTo("diagnostic");
        var host = (SettingsLogPageHost)GetLogPageHost(controller);
        source = AttachControllableAggregationSource(host, installRoot);
        SetPrivateProperty(
          host,
          "SessionQueryOverrideForTests",
          (Func<LogSessionMetadata, LogQueryExpression, CancellationToken, Task<IReadOnlyList<LogEvent>>>)
          ((session, _, _) => Task.FromResult<IReadOnlyList<LogEvent>>([
            CreateEvent("session-query", session.StartTimestamp.AddMinutes(1), "SessionQuery", "session query")
          ])));
        var statisticsInspector = RequireStatisticsInspector(window);
        var componentDistribution = RequireComponentDistribution(window);
        var firstStart = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.FromHours(8));
        var firstSession = CreateSession("session-a", firstStart, firstStart.AddMinutes(3));

        statisticsInspector.IsExpanded = true;
        WpfStaTestHost.WaitUntil(
          () => source.FlightCount == 1,
          TimeSpan.FromSeconds(10),
          "initial expanded statistics generation to start");

        InvokePrivate(host, "SelectSession", firstSession);
        WpfStaTestHost.WaitUntil(
          () => source.FlightCount == 2,
          TimeSpan.FromSeconds(10),
          "session enter to start a scoped statistics generation");
        WpfStaTestHost.WaitUntil(
          () => source.FlightAt(0).WasCancelled,
          TimeSpan.FromSeconds(10),
          "session enter to cancel the previous statistics generation");
        source.ReleaseFlight(
          1,
          CreateEvent("session-enter-latest", firstStart.AddMinutes(1), "SessionEnterLatest", "session enter latest"));
        WpfStaTestHost.WaitUntil(
          () => ComponentNames(componentDistribution).Contains("SessionEnterLatest"),
          TimeSpan.FromSeconds(10),
          "session-enter scoped statistics result to render");

        InvokePrivate(host, "OnFilterRefresh", false);
        WpfStaTestHost.WaitUntil(
          () => source.FlightCount == 3,
          TimeSpan.FromSeconds(10),
          "pre-metadata statistics generation to start");
        var updatedSession = firstSession with { EndTimestamp = firstStart.AddMinutes(2) };
        InvokePrivate(host, "RefreshActiveSessionMetadata", updatedSession);
        WpfStaTestHost.WaitUntil(
          () => source.FlightCount == 4,
          TimeSpan.FromSeconds(10),
          "active metadata update to start a refreshed scoped statistics generation");
        WpfStaTestHost.WaitUntil(
          () => source.FlightAt(2).WasCancelled,
          TimeSpan.FromSeconds(10),
          "active metadata update to cancel the stale statistics generation");
        source.ReleaseFlight(
          3,
          CreateEvent("metadata-latest", firstStart.AddMinutes(1), "MetadataLatest", "metadata latest"));
        WpfStaTestHost.WaitUntil(
          () => ComponentNames(componentDistribution).Contains("MetadataLatest"),
          TimeSpan.FromSeconds(10),
          "metadata-refresh scoped statistics result to render");

        InvokePrivate(host, "OnFilterRefresh", false);
        WpfStaTestHost.WaitUntil(
          () => source.FlightCount == 5,
          TimeSpan.FromSeconds(10),
          "pre-exit statistics generation to start");
        InvokePrivate(host, "RestoreSessionView", true);
        WpfStaTestHost.WaitUntil(
          () => source.FlightCount == 6,
          TimeSpan.FromSeconds(10),
          "session exit to start an all-sessions statistics generation");
        WpfStaTestHost.WaitUntil(
          () => source.FlightAt(4).WasCancelled,
          TimeSpan.FromSeconds(10),
          "session exit to cancel the stale scoped statistics generation");
        source.ReleaseFlight(
          5,
          CreateEvent("session-exit-latest", firstStart.AddMinutes(5), "SessionExitLatest", "session exit latest"));
        WpfStaTestHost.WaitUntil(
          () => ComponentNames(componentDistribution).Contains("SessionExitLatest"),
          TimeSpan.FromSeconds(10),
          "session-exit all-sessions statistics result to render");

        ComponentNames(componentDistribution).Should().Contain("SessionExitLatest");
        ComponentNames(componentDistribution).Should().NotContain("MetadataLatest");
      }
      finally
      {
        source?.ReleaseAll();
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

  private static ControllableAggregationEventSource AttachControllableAggregationSource(
    SettingsLogPageHost host,
    string installRoot)
  {
    var source = new ControllableAggregationEventSource();
    SetPrivateField(
      host,
      "_logController",
      new LogViewController(source, Path.Combine(installRoot, "SmartGuard.log")));
    return source;
  }

  private static Expander RequireStatisticsInspector(Window window)
    => window.FindName("logStatisticsInspector") as Expander
       ?? throw new InvalidOperationException("logStatisticsInspector was not found.");

  private static ItemsControl RequireComponentDistribution(Window window)
    => window.FindName("logComponentDistribution") as ItemsControl
       ?? throw new InvalidOperationException("logComponentDistribution was not found.");

  private static IReadOnlyList<string> ComponentNames(ItemsControl componentDistribution)
  {
    if (componentDistribution.ItemsSource is not IEnumerable<object> items)
      return [];

    return items
      .Select(item => item.GetType().GetProperty("Name")?.GetValue(item) as string)
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .Select(name => name!)
      .ToList();
  }

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

  private sealed class ControllableAggregationEventSource : ILogEventSource
  {
    private readonly object _lock = new();
    private readonly List<AggregationFlight> _flights = [];

    internal int FlightCount
    {
      get
      {
        lock (_lock)
          return _flights.Count;
      }
    }

    internal AggregationFlight FlightAt(int index)
    {
      lock (_lock)
        return _flights[index];
    }

    public Task<LogEventPage> ReadLatestAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadOlderAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadNewerAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public async IAsyncEnumerable<LogEvent> StreamAsync(
      LogReadScope scope,
      [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var flight = new AggregationFlight(cancellationToken);
      lock (_lock)
        _flights.Add(flight);

      var events = await flight.WaitForReleaseAsync(cancellationToken);
      foreach (var logEvent in events)
        yield return logEvent;
    }

    internal void ReleaseFlight(int index, params LogEvent[] events)
      => FlightAt(index).Release(events);

    internal void ReleaseAll()
    {
      lock (_lock)
      {
        foreach (var flight in _flights)
          flight.Release([]);
      }
    }
  }

  private sealed class AggregationFlight
  {
    private readonly CancellationTokenRegistration _registration;
    private readonly TaskCompletionSource<IReadOnlyList<LogEvent>> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal AggregationFlight(CancellationToken cancellationToken)
    {
      _registration = cancellationToken.Register(() => _cancelled.TrySetResult());
    }

    internal bool WasCancelled => _cancelled.Task.IsCompleted;

    internal async Task<IReadOnlyList<LogEvent>> WaitForReleaseAsync(CancellationToken cancellationToken)
    {
      try
      {
        return await _release.Task.WaitAsync(cancellationToken);
      }
      finally
      {
        _registration.Dispose();
      }
    }

    internal void Release(IReadOnlyList<LogEvent> events)
      => _release.TrySetResult(events);
  }
}
