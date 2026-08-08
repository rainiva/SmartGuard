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

    private static LogSearchFilterBar GetSearchFilterBar(object host)
    {
        var field = host.GetType().GetField(
            "_logSearchFilterBar",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (LogSearchFilterBar)field!.GetValue(host)!;
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

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
