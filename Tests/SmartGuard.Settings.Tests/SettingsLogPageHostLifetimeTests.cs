using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public sealed class SettingsLogPageHostLifetimeTests
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public void Dispose_during_blocked_refresh_releases_background_lifetime_without_applying_to_ui()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            using var readEntered = new ManualResetEventSlim();
            using var releaseRead = new ManualResetEventSlim();
            SettingsWindowController? controller = null;

            try
            {
                WriteLog(installRoot);
                LogTailReaderTestMetrics.Reset();
                LogTailReaderTestMetrics.BeforeReadForTests = () =>
                {
                    readEntered.Set();
                    if (!releaseRead.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Test did not release the blocked log read.");
                };

                controller = CreateController(installRoot);
                WpfStaTestHost.Track(controller);
                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);

                controller.NavigateTo("logs");
                WpfStaTestHost.WaitUntil(
                    () => readEntered.IsSet,
                    TimeSpan.FromSeconds(10),
                    "the background log read to start");

                var disposeDuration = Stopwatch.StartNew();
                controller.Dispose();
                disposeDuration.Stop();

                disposeDuration.Elapsed.Should().BeLessThan(
                    TimeSpan.FromMilliseconds(200),
                    "Dispose must cancel ownership without synchronously waiting for disk I/O");

                releaseRead.Set();
                WpfStaTestHost.WaitUntil(
                    () => !controller.IsLogRefreshInFlightForTests,
                    TimeSpan.FromSeconds(10),
                    "the background log refresh lifetime to finish");

                controller.ActiveLogRefreshCountForTests.Should().Be(0);
                WpfStaTestHost.PumpDispatcher(window);
                controller.ApplyAfterDisposeCountForTests.Should().Be(0);
            }
            finally
            {
                releaseRead.Set();
                controller?.Dispose();
                LogTailReaderTestMetrics.Reset();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Read_failure_exits_loading_state_and_shows_failure_status()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            SettingsWindowController? controller = null;

            try
            {
                WriteLog(installRoot);
                LogTailReaderTestMetrics.Reset();
                var readAttempt = 0;
                LogTailReaderTestMetrics.BeforeReadForTests = () =>
                {
                    if (Interlocked.Increment(ref readAttempt) == 1)
                        throw new IOException("simulated log read failure");
                };

                controller = CreateController(installRoot);
                WpfStaTestHost.Track(controller);
                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);

                controller.NavigateTo("logs");
                var status = window.FindName("lblLogStatus") as TextBlock;
                status.Should().NotBeNull();

                WpfStaTestHost.WaitUntil(
                    () => status!.Text.Contains("加载失败", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(10),
                    "the log page to show its failed loading state");

                status!.Text.Should().Contain("加载失败");
                controller.IsLogRefreshInFlightForTests.Should().BeFalse();
            }
            finally
            {
                controller?.Dispose();
                LogTailReaderTestMetrics.Reset();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Refresh_result_still_shows_when_new_refresh_requests_keep_arriving()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            using var releaseSecondRead = new ManualResetEventSlim();
            SettingsWindowController? controller = null;

            try
            {
                WriteLog(installRoot);
                LogTailReaderTestMetrics.Reset();

                controller = CreateController(installRoot);
                WpfStaTestHost.Track(controller);
                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);

                // Simulate the 2s auto-refresh timer beating the UI completion
                // callback on a loaded machine: while the first flight is
                // reading (its reads are attempts 1-2), a new refresh request
                // is injected, so the first flight's generation is already
                // stale when it completes. The restarted flight (attempt 3+)
                // then blocks on its read, freezing the moment after the stale
                // completion so the assertion below is deterministic.
                var readCount = 0;
                var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
                var capturedController = controller;
                var capturedList = (ListBox?)null;
                LogTailReaderTestMetrics.BeforeReadForTests = () =>
                {
                    var attempt = Interlocked.Increment(ref readCount);
                    events.Enqueue($"read#{attempt} on t{Environment.CurrentManagedThreadId}");
                    if (attempt == 1)
                        window.Dispatcher.BeginInvoke(() =>
                        {
                            events.Enqueue($"inject ForceRefresh, items={capturedList?.Items.Count ?? -1}, inFlight={capturedController.IsLogRefreshInFlightForTests}");
                            capturedController.ForceRefreshLogView();
                        });
                    else if (attempt >= 3 && !releaseSecondRead.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Test did not release the blocked restarted log read.");
                };

                controller.NavigateTo("logs");
                var lstLogView = window.FindName("lstLogView") as ListBox;
                lstLogView.Should().NotBeNull();
                capturedList = lstLogView;

                try
                {
                    WpfStaTestHost.WaitUntil(
                        () => lstLogView!.Items.Count > 0,
                        TimeSpan.FromSeconds(5),
                        "the first completed refresh to show log items even though a newer refresh request arrived meanwhile");
                }
                catch (TimeoutException ex)
                {
                    throw new TimeoutException(
                        $"{ex.Message} events=[{string.Join(" | ", events)}]",
                        ex);
                }

                releaseSecondRead.Set();
                WpfStaTestHost.WaitUntil(
                    () => !controller.IsLogRefreshInFlightForTests,
                    TimeSpan.FromSeconds(10),
                    "the refresh pipeline to settle after the blocked read is released");
            }
            finally
            {
                releaseSecondRead.Set();
                controller?.Dispose();
                LogTailReaderTestMetrics.Reset();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Dispose_can_be_called_twice()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            SettingsWindowController? controller = null;

            try
            {
                controller = CreateController(installRoot);
                WpfStaTestHost.Track(controller);
                controller.Dispose();

                var secondDispose = () => controller.Dispose();

                secondDispose.Should().NotThrow();
            }
            finally
            {
                controller?.Dispose();
                LogTailReaderTestMetrics.Reset();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Late_session_query_after_switch_does_not_replace_active_session_results()
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
                WpfStaTestHost.WaitUntil(
                    () => controller.IsLogViewInitializedForTests,
                    TimeSpan.FromSeconds(10),
                    "the log view to initialize");
                var list = (ListBox)window.FindName("lstLogView")!;
                WpfStaTestHost.WaitUntil(
                    () => list.Items.Count > 0,
                    TimeSpan.FromSeconds(10),
                    "the initial log refresh to apply");

                var host = GetLogPageHost(controller);
                var queryOverride = host.GetType().GetProperty(
                    "SessionQueryOverrideForTests",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                queryOverride.Should().NotBeNull(
                    "host race tests need a controllable session query seam instead of fixed delays");

                var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
                var sessionA = CreateSession("s:a", baseTime);
                var sessionB = CreateSession("s:b", baseTime.AddHours(1));
                var aResult = new TaskCompletionSource<IReadOnlyList<LogEvent>>();
                var bResult = new TaskCompletionSource<IReadOnlyList<LogEvent>>();
                var bQueryStarted = new TaskCompletionSource();
                queryOverride!.SetValue(host, new Func<LogSessionMetadata, LogQueryExpression, CancellationToken, Task<IReadOnlyList<LogEvent>>>(
                    (session, _, token) =>
                    {
                        token.Register(() =>
                        {
                            if (session.SessionId == sessionA.SessionId)
                                aResult.TrySetCanceled(token);
                        });
                        if (session.SessionId == sessionB.SessionId)
                        {
                            bQueryStarted.TrySetResult();
                            return bResult.Task;
                        }

                        return aResult.Task;
                    }));

                InvokePrivate(host, "SelectSession", sessionA);
                InvokePrivate(host, "SelectSession", sessionB);
                WpfStaTestHost.WaitUntil(
                    () => bQueryStarted.Task.IsCompleted,
                    TimeSpan.FromSeconds(10),
                    "session B query to start");
                bResult.SetResult([CreateEvent("b:event", baseTime.AddHours(1).AddMinutes(1), "session B result")]);

                WpfStaTestHost.WaitUntil(
                    () => list.Items.OfType<LogLineDisplayItem>().Any(item => item.EventId == "b:event"),
                    TimeSpan.FromSeconds(10),
                    "session B result to apply");

                aResult.TrySetResult([CreateEvent("a:event", baseTime.AddMinutes(1), "session A late result")]);
                WpfStaTestHost.PumpDispatcher(window);

                list.Items.OfType<LogLineDisplayItem>().Should().Contain(item => item.EventId == "b:event");
                list.Items.OfType<LogLineDisplayItem>().Should().NotContain(item => item.EventId == "a:event");
            }
            finally
            {
                controller?.Dispose();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Dispose_during_session_query_cancels_without_late_apply()
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

                var host = GetLogPageHost(controller);
                var queryOverride = host.GetType().GetProperty(
                    "SessionQueryOverrideForTests",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                queryOverride.Should().NotBeNull(
                    "session query cancellation must be observable without fixed sleeps");

                var queryStarted = new TaskCompletionSource();
                var releaseQuery = new TaskCompletionSource<IReadOnlyList<LogEvent>>();
                queryOverride!.SetValue(host, new Func<LogSessionMetadata, LogQueryExpression, CancellationToken, Task<IReadOnlyList<LogEvent>>>(
                    (_, _, token) =>
                    {
                        queryStarted.SetResult();
                        token.Register(() => releaseQuery.TrySetCanceled(token));
                        return releaseQuery.Task;
                    }));

                InvokePrivate(host, "SelectSession", CreateSession("s:dispose", new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero)));
                queryStarted.Task.GetAwaiter().GetResult();

                controller.Dispose();
                releaseQuery.TrySetResult([CreateEvent("late:event", new DateTimeOffset(2026, 8, 2, 10, 1, 0, TimeSpan.Zero), "late")]);
                WpfStaTestHost.PumpDispatcher(window);

                controller.ApplyAfterDisposeCountForTests.Should().Be(0);
            }
            finally
            {
                controller?.Dispose();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Dispose_during_correlation_query_cancels_without_late_apply()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            SettingsWindowController? controller = null;
            var eventSource = new ControllableCorrelationEventSource();

            try
            {
                WriteLog(installRoot);
                controller = CreateController(installRoot);
                WpfStaTestHost.Track(controller);
                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);
                controller.NavigateTo("logs");
                WpfStaTestHost.WaitUntil(
                    () => controller.IsLogViewInitializedForTests && !controller.IsLogRefreshInFlightForTests,
                    TimeSpan.FromSeconds(5),
                    "initial log view refresh to settle before replacing the controller with a controllable fake");
                CollapseStatisticsInspector(window);

                var host = GetLogPageHost(controller);
                StopLogTimer(host);
                var logController = new LogViewController(
                    eventSource,
                    Path.Combine(installRoot, "SmartGuard.log"));
                SetPrivateField(host, "_logController", logController);
                var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);

                InvokePrivate(host, "EnterCorrelationView", CreateCorrelationEvent("correlation:dispose-anchor", baseTime, "op-dispose"));
                WpfStaTestHost.WaitUntil(
                    () => eventSource.FlightCount == 1,
                    TimeSpan.FromSeconds(5),
                    "blocked correlation query to start");

                controller.Dispose();

                WpfStaTestHost.WaitUntil(
                    () => eventSource.FlightAt(0).WasCancelled,
                    TimeSpan.FromSeconds(5),
                    "Dispose to cancel the in-flight correlation query");

                eventSource.ReleaseFlight(0, CreateCorrelationEvent("correlation:late-after-dispose", baseTime.AddMinutes(1), "op-dispose"));
                WpfStaTestHost.PumpDispatcher(window);

                controller.ApplyAfterDisposeCountForTests.Should().Be(0);
                logController.EventWindowForTests.Events.Should().NotContain(logEvent => logEvent.EventId == "correlation:late-after-dispose");
            }
            finally
            {
                eventSource.ReleaseAll();
                controller?.Dispose();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Selecting_session_while_correlation_query_is_in_flight_cancels_correlation_and_keeps_session_active()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            SettingsWindowController? controller = null;
            var eventSource = new ControllableCorrelationEventSource();

            try
            {
                WriteLog(installRoot);
                controller = CreateController(installRoot);
                WpfStaTestHost.Track(controller);
                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);
                controller.NavigateTo("logs");
                WpfStaTestHost.WaitUntil(
                    () => controller.IsLogViewInitializedForTests && !controller.IsLogRefreshInFlightForTests,
                    TimeSpan.FromSeconds(5),
                    "initial log view refresh to settle before replacing the controller with a controllable fake");
                CollapseStatisticsInspector(window);

                var host = GetLogPageHost(controller);
                StopLogTimer(host);
                var logController = new LogViewController(
                    eventSource,
                    Path.Combine(installRoot, "SmartGuard.log"));
                SetPrivateField(host, "_logController", logController);
                SetPrivateProperty(
                    host,
                    "SessionQueryOverrideForTests",
                    (Func<LogSessionMetadata, LogQueryExpression, CancellationToken, Task<IReadOnlyList<LogEvent>>>)
                    ((session, _, _) => Task.FromResult<IReadOnlyList<LogEvent>>([
                        CreateEvent("session:event", session.StartTimestamp.AddMinutes(1), "session result")
                    ])));

                var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
                InvokePrivate(host, "EnterCorrelationView", CreateCorrelationEvent("correlation:anchor", baseTime, "op-session-switch"));
                WpfStaTestHost.WaitUntil(
                    () => eventSource.FlightCount == 1,
                    TimeSpan.FromSeconds(5),
                    "correlation query to start with a cancellable token");

                var session = CreateSession("s:from-correlation", baseTime);
                InvokePrivate(host, "SelectSession", session);

                WpfStaTestHost.WaitUntil(
                    () => eventSource.FlightAt(0).WasCancelled,
                    TimeSpan.FromSeconds(5),
                    "selecting a session to cancel the in-flight correlation query");
                GetCorrelationCoordinator(host).IsActive.Should().BeFalse(
                    "session and correlation drilldown modes must be mutually exclusive");
                GetSessionCoordinator(host).IsActive.Should().BeTrue();
                logController.ScopeExpression.Should().NotBeNull();
                LogQueryExpressionEvaluator.Matches(
                    CreateEvent("session:inside", baseTime.AddMinutes(1), "session result"),
                    logController.ScopeExpression!).Should().BeTrue();
                LogQueryExpressionEvaluator.Matches(
                    CreateEvent("session:outside", baseTime.AddHours(1), "session result"),
                    logController.ScopeExpression!).Should().BeFalse();
            }
            finally
            {
                eventSource.ReleaseAll();
                controller?.Dispose();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Entering_correlation_while_session_query_is_in_flight_cancels_session_and_uses_correlation_scope()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            SettingsWindowController? controller = null;
            var eventSource = new ControllableCorrelationEventSource();

            try
            {
                WriteLog(installRoot);
                controller = CreateController(installRoot);
                WpfStaTestHost.Track(controller);
                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);
                controller.NavigateTo("logs");
                WpfStaTestHost.WaitUntil(
                    () => controller.IsLogViewInitializedForTests && !controller.IsLogRefreshInFlightForTests,
                    TimeSpan.FromSeconds(5),
                    "initial log view refresh to settle before replacing the controller with a controllable fake");
                CollapseStatisticsInspector(window);

                var host = GetLogPageHost(controller);
                StopLogTimer(host);
                var logController = new LogViewController(
                    eventSource,
                    Path.Combine(installRoot, "SmartGuard.log"));
                SetPrivateField(host, "_logController", logController);
                var sessionQueryStarted = new TaskCompletionSource();
                var sessionCancelled = new TaskCompletionSource();
                var sessionResult = new TaskCompletionSource<IReadOnlyList<LogEvent>>();
                SetPrivateProperty(
                    host,
                    "SessionQueryOverrideForTests",
                    (Func<LogSessionMetadata, LogQueryExpression, CancellationToken, Task<IReadOnlyList<LogEvent>>>)
                    ((_, _, token) =>
                    {
                        sessionQueryStarted.SetResult();
                        token.Register(() =>
                        {
                            sessionCancelled.TrySetResult();
                            sessionResult.TrySetCanceled(token);
                        });
                        return sessionResult.Task;
                    }));

                var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
                InvokePrivate(host, "SelectSession", CreateSession("s:to-correlation", baseTime));
                sessionQueryStarted.Task.GetAwaiter().GetResult();

                InvokePrivate(host, "EnterCorrelationView", CreateCorrelationEvent("correlation:anchor", baseTime, "op-correlation-switch"));
                WpfStaTestHost.WaitUntil(
                    () => sessionCancelled.Task.IsCompleted,
                    TimeSpan.FromSeconds(5),
                    "entering correlation to cancel the in-flight session query");
                WpfStaTestHost.WaitUntil(
                    () => eventSource.FlightCount == 1,
                    TimeSpan.FromSeconds(5),
                    "correlation query to start after session cancellation");

                GetSessionCoordinator(host).IsActive.Should().BeFalse(
                    "correlation and session drilldown modes must be mutually exclusive");
                GetCorrelationCoordinator(host).IsActive.Should().BeTrue();
                logController.ScopeExpression.Should().NotBeNull();
                LogQueryExpressionEvaluator.Matches(
                    CreateCorrelationEvent("correlation:inside", baseTime.AddMinutes(1), "op-correlation-switch"),
                    logController.ScopeExpression!).Should().BeTrue();
                LogQueryExpressionEvaluator.Matches(
                    CreateCorrelationEvent("correlation:outside", baseTime.AddMinutes(1), "other-op"),
                    logController.ScopeExpression!).Should().BeFalse();
            }
            finally
            {
                eventSource.ReleaseAll();
                controller?.Dispose();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Late_correlation_query_after_switch_does_not_replace_latest_correlation_results()
    {
        WpfStaTestHost.Run(() =>
        {
            var installRoot = CreateInstallRoot();
            SettingsWindowController? controller = null;
            var eventSource = new ControllableCorrelationEventSource();

            try
            {
                WriteLog(installRoot);
                controller = CreateController(installRoot);
                WpfStaTestHost.Track(controller);
                var window = GetWindow(controller);
                WpfStaTestHost.ShowAndWait(window);
                controller.NavigateTo("logs");
                WpfStaTestHost.WaitUntil(
                    () => controller.IsLogViewInitializedForTests && !controller.IsLogRefreshInFlightForTests,
                    TimeSpan.FromSeconds(5),
                    "initial log view refresh to settle before replacing the controller with a controllable fake");
                CollapseStatisticsInspector(window);

                var host = GetLogPageHost(controller);
                StopLogTimer(host);
                var logController = new LogViewController(
                    eventSource,
                    Path.Combine(installRoot, "SmartGuard.log"));
                SetPrivateField(host, "_logController", logController);
                var searchBar = GetSearchFilterBar(host);
                searchBar.SetKeyword(string.Empty);
                searchBar.ClearTagFilters();
                logController.SearchKeyword = string.Empty;
                logController.ActiveTagFilters = [];
                logController.SearchCaseSensitive = false;
                logController.TimeRange = LogViewTimeRange.All;
                logController.CustomRangeStart = null;
                logController.CustomRangeEnd = null;
                SetTimeRangeComboToAll(host);
                SetCaseSensitiveToFalse(host);
                var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);

                InvokePrivate(host, "EnterCorrelationView", CreateCorrelationEvent("correlation:a-anchor", baseTime, "op-a"));
                WpfStaTestHost.WaitUntil(
                    () => eventSource.FlightCount == 1,
                    TimeSpan.FromSeconds(5),
                    "correlation A query to start");

                InvokePrivate(host, "EnterCorrelationView", CreateCorrelationEvent("correlation:b-anchor", baseTime, "op-b"));
                WpfStaTestHost.WaitUntil(
                    () => eventSource.FlightCount == 2,
                    TimeSpan.FromSeconds(5),
                    "correlation B query to start");
                WpfStaTestHost.WaitUntil(
                    () => eventSource.FlightAt(0).WasCancelled,
                    TimeSpan.FromSeconds(5),
                    "correlation B to cancel correlation A");

                eventSource.ReleaseFlight(1, CreateCorrelationEvent("correlation:b-result", baseTime.AddMinutes(1), "op-b"));
                WpfStaTestHost.WaitUntil(
                    () => logController.EventWindowForTests.Events.Any(logEvent => logEvent.EventId == "correlation:b-result"),
                    TimeSpan.FromSeconds(5),
                    "latest correlation B result to reach the active controller event window");
                WpfStaTestHost.WaitUntil(
                    () => GetLastDisplayedLines(host).Any(line => line.Contains("op-b", StringComparison.Ordinal)),
                    TimeSpan.FromSeconds(5),
                    "latest correlation B result to render in displayed lines");
                eventSource.ReleaseFlight(0, CreateCorrelationEvent("correlation:a-late", baseTime.AddMinutes(1), "op-a"));
                WpfStaTestHost.PumpDispatcher(window);

                logController.EventWindowForTests.Events.Should().Contain(logEvent => logEvent.EventId == "correlation:b-result");
                logController.EventWindowForTests.Events.Should().NotContain(logEvent => logEvent.EventId == "correlation:a-late");
                GetLastDisplayedLines(host).Should().Contain(line => line.Contains("op-b", StringComparison.Ordinal));
                GetLastDisplayedLines(host).Should().NotContain(line => line.Contains("op-a", StringComparison.Ordinal));
                logController.ScopeExpression.Should().NotBeNull();
                LogQueryExpressionEvaluator.Matches(
                    CreateCorrelationEvent("correlation:b-inside", baseTime.AddMinutes(2), "op-b"),
                    logController.ScopeExpression!).Should().BeTrue();
                LogQueryExpressionEvaluator.Matches(
                    CreateCorrelationEvent("correlation:a-outside", baseTime.AddMinutes(2), "op-a"),
                    logController.ScopeExpression!).Should().BeFalse();
            }
            finally
            {
                eventSource.ReleaseAll();
                controller?.Dispose();
                TryDelete(installRoot);
            }
        });
    }

    [Fact]
    public void Active_session_statistics_use_current_filter_expression_and_session_clause()
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

                var host = GetLogPageHost(controller);
                var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
                var session = CreateSession("s:stats", baseTime);
                var savedMainView = new LogSessionMainViewState(
                    BaseExpression: LogQueryParser.Parse("message:saved-only").Expression!,
                    SearchKeyword: "saved-only",
                    ActiveTags: [],
                    TimeRange: LogViewTimeRange.All,
                    CustomRangeStart: null,
                    CustomRangeEnd: null,
                    SearchCaseSensitive: false,
                    Anchor: null,
                    SelectedEventId: null);
                GetSessionCoordinator(host).SelectSession(session, savedMainView);

                GetSearchFilterBar(host).SetKeyword("current-only");
                var request = InvokeBuildAggregationRequest(host);

                LogQueryExpressionEvaluator.Matches(
                    CreateEvent("current", baseTime.AddMinutes(1), "current-only"),
                    request.Expression).Should().BeTrue();
                LogQueryExpressionEvaluator.Matches(
                    CreateEvent("saved", baseTime.AddMinutes(2), "saved-only"),
                    request.Expression).Should().BeFalse(
                    "statistics must not keep using the expression saved when entering the session");
                LogQueryExpressionEvaluator.Matches(
                    CreateEvent("outside", baseTime.AddHours(1), "current-only"),
                    request.Expression).Should().BeFalse(
                    "statistics must still enforce the active session boundary");
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
            ?? throw new InvalidOperationException("Failed to create SettingsWindowController for WPF lifetime test.");
    }

    private static string CreateInstallRoot()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "sg-log-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
        return installRoot;
    }

    private static void WriteLog(string installRoot)
    {
        File.WriteAllText(
            Path.Combine(installRoot, "SmartGuard.log"),
            "[INFO] 2026-07-26 10:00:00 lifetime test line\n",
            Utf8NoBom);
    }

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

    private static LogSessionCoordinator GetSessionCoordinator(object host)
    {
        var property = host.GetType().GetProperty(
            "SessionCoordinatorForTests",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (LogSessionCoordinator)property!.GetValue(host)!;
    }

    private static LogCorrelationCoordinator GetCorrelationCoordinator(object host)
    {
        var property = host.GetType().GetProperty(
            "CorrelationCoordinatorForTests",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (LogCorrelationCoordinator)property!.GetValue(host)!;
    }

    private static LogSearchFilterBar GetSearchFilterBar(object host)
    {
        var field = host.GetType().GetField(
            "_logSearchFilterBar",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (LogSearchFilterBar)field!.GetValue(host)!;
    }

    private static IReadOnlyList<string> GetLastDisplayedLines(object host)
    {
        var field = host.GetType().GetField(
            "_lastDisplayedLines",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (IReadOnlyList<string>)field!.GetValue(host)!;
    }

    private static void CollapseStatisticsInspector(Window window)
    {
        if (window.FindName("logStatisticsInspector") is Expander statisticsInspector)
            statisticsInspector.IsExpanded = false;
    }

    private static void StopLogTimer(object host)
    {
        var field = host.GetType().GetField(
            "_logTimer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(host) is System.Windows.Threading.DispatcherTimer timer)
            timer.Stop();
    }

    private static void SetTimeRangeComboToAll(object host)
    {
        var field = host.GetType().GetField(
            "_cmbLogTimeRange",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(host) is ComboBox combo)
            combo.SelectedIndex = 0;
    }

    private static void SetCaseSensitiveToFalse(object host)
    {
        var field = host.GetType().GetField(
            "_chkLogSearchCaseSensitive",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(host) is CheckBox checkBox)
            checkBox.IsChecked = false;
    }

    private static LogAggregationRequest InvokeBuildAggregationRequest(object host)
    {
        var method = host.GetType().GetMethod(
            "BuildAggregationRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        return (LogAggregationRequest)method!.Invoke(host, [])!;
    }

    private static void InvokePrivate(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        method!.Invoke(instance, args);
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

    private static LogSessionMetadata CreateSession(string sessionId, DateTimeOffset start)
        => new(
            sessionId,
            start,
            start.AddMinutes(30),
            100,
            LogComponentRegistry.Engine,
            false,
            sessionId + ":start",
            sessionId + ":end");

    private static LogEvent CreateEvent(string eventId, DateTimeOffset timestamp, string message)
        => new(
            LogEvent.CurrentSchemaVersion,
            eventId,
            timestamp,
            LogSeverity.Info,
            LogComponentRegistry.Engine,
            LogChannelRegistry.Lifecycle,
            EventName: null,
            message,
            Exception: null,
            OperationId: null,
            TraceId: null,
            SpanId: null,
            UserActionId: null,
            ProcessId: 100,
            ThreadId: null,
            LogEvent.EmptyProperties);

    private static LogEvent CreateCorrelationEvent(
        string eventId,
        DateTimeOffset timestamp,
        string operationId)
        => new(
            LogEvent.CurrentSchemaVersion,
            eventId,
            timestamp,
            LogSeverity.Info,
            LogComponentRegistry.Engine,
            LogChannelRegistry.Lifecycle,
            EventName: null,
            "correlation",
            Exception: null,
            operationId,
            TraceId: null,
            SpanId: null,
            UserActionId: null,
            ProcessId: 100,
            ThreadId: null,
            LogEvent.EmptyProperties);

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed class ControllableCorrelationEventSource : ILogEventSource
    {
        private readonly object _lock = new();
        private readonly List<CorrelationFlight> _flights = [];

        internal int FlightCount
        {
            get
            {
                lock (_lock)
                    return _flights.Count;
            }
        }

        internal CorrelationFlight FlightAt(int index)
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
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var flight = new CorrelationFlight(cancellationToken);
            lock (_lock)
                _flights.Add(flight);

            var events = await flight.WaitForReleaseAsync(cancellationToken);
            foreach (var logEvent in events)
                yield return logEvent;
        }

        internal void ReleaseAll()
        {
            lock (_lock)
            {
                foreach (var flight in _flights)
                    flight.Release([]);
            }
        }

        internal void ReleaseFlight(int index, params LogEvent[] events)
            => FlightAt(index).Release(events);
    }

    private sealed class CorrelationFlight
    {
        private readonly CancellationTokenRegistration _registration;
        private readonly TaskCompletionSource<IReadOnlyList<LogEvent>> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal CorrelationFlight(CancellationToken cancellationToken)
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
            catch (OperationCanceledException)
            {
                _cancelled.TrySetResult();
                throw;
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
