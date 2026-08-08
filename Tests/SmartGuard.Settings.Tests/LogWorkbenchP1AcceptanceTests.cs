using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartGuard.Configuration;
using SmartGuard.Settings;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class LogWorkbenchP1AcceptanceTests
{
  private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

  private static LogMatch Match(int ordinal, string eventId)
    => new(ordinal, eventId, LogSourceIdentity.FromContentPrefix(DateTimeOffset.UtcNow, eventId), 0);

  [Fact]
  public void Query_generation_survives_50_rapid_switches_without_stale_matches()
  {
    var navigator = new LogMatchNavigator();

    for (var generation = 1; generation <= 50; generation++)
    {
      navigator.NotifyQueryGenerationChanged(generation);
      navigator.CurrentMatches.Should().BeEmpty();
    }

    navigator.SetMatches(
      queryGeneration: 50,
      [Match(1, "final-event")],
      scanComplete: true,
      visibleEventIds: ["final-event"]);

    navigator.NavigateNext(reverse: false).TargetEventId.Should().Be("final-event");
    navigator.CurrentMatches.Should().ContainSingle(m => m.EventId == "final-event");
  }

  [Fact]
  public async Task Large_fixture_supports_combo_query_regex_and_paging()
  {
    const int archiveCount = 30;
    const int eventsPerArchive = 2000;
    var fixture = LogWorkbenchPerformanceFixture.Create(
      archiveCount: archiveCount,
      eventsPerArchive: eventsPerArchive,
      currentLogTargetBytes: 10 * 1024 * 1024);

    try
    {
      var reader = new LogTailReader(fixture.LogPath);
      var latest = await reader.ReadLatestAsync(
        new LogReadRequest(null, LogReadDirection.Latest, 200, LogTailReader.DefaultMaxBytes),
        CancellationToken.None);
      latest.Events.Should().NotBeEmpty();
      latest.HasOlder.Should().BeTrue();

      var older = await reader.ReadOlderAsync(
        new LogReadRequest(latest.OlderCursor, LogReadDirection.Older, 200, LogTailReader.DefaultMaxBytes),
        CancellationToken.None);
      older.Events.Should().NotBeEmpty();
      older.Events.Select(e => e.EventId).Should().OnlyHaveUniqueItems();

      var expression = LogQueryParser.Parse(
        "component:Engine AND (message~archive OR message~current)").Expression!;
      var service = new LogQueryService();
      var queryResult = await service.QueryAsync(
        new LogTailReaderEventSource(reader),
        expression,
        cursor: null,
        pageSize: 50,
        progress: null,
        CancellationToken.None);

      queryResult.Matches.Should().NotBeEmpty();
      queryResult.IsScanComplete.Should().BeTrue();

      var snapshot = reader.CaptureSnapshot();
      var index = await service.BuildPageIndexAsync(
        reader,
        expression,
        new LogReadScope(Array.Empty<LogSourceIdentity>()),
        snapshot,
        ignoreCase: true,
        progress: null,
        CancellationToken.None);
      var numberedPage = await service.ReadPageAsync(
        reader,
        index,
        new LogQueryPageRequest(1, 50, LogSortDirection.NewestFirst),
        CancellationToken.None);
      var oldestPage = await service.ReadPageAsync(
        reader,
        index,
        new LogQueryPageRequest(1, 50, LogSortDirection.OldestFirst),
        CancellationToken.None);
      var clampedPage = await service.ReadPageAsync(
        reader,
        index,
        new LogQueryPageRequest(int.MaxValue, 50, LogSortDirection.NewestFirst),
        CancellationToken.None);

      numberedPage.Events.Should().NotBeEmpty();
      numberedPage.Events.Should().HaveCount(50);
      numberedPage.ExactTotalCount.Should().Be(index.ExactTotalCount);
      // LogUtf8LineScanner preserves each file's BOM in its first physical line;
      // the component predicate therefore excludes one first-line event from each
      // archive and the current log.
      var expectedArchiveMatches = archiveCount * (eventsPerArchive - 1);
      var expectedCurrentMatches = fixture.CurrentEventCount - 1;
      index.ExactTotalCount.Should().Be(expectedArchiveMatches + expectedCurrentMatches);
      numberedPage.Events.Should().OnlyContain(logEvent => logEvent.Message.StartsWith("current-", StringComparison.Ordinal));
      numberedPage.Events[0].Message.Should().Be("current-tail");
      numberedPage.Events.Select(logEvent => logEvent.Timestamp).Should().BeInDescendingOrder();

      oldestPage.Events.Select(logEvent => logEvent.Message).Should().Equal(
        Enumerable.Range(1, 50).Select(index => $"archive-{index}"));

      clampedPage.PageNumber.Should().Be(clampedPage.TotalPages);
      clampedPage.Events.Should().HaveCount(
        index.ExactTotalCount % 50 == 0 ? 50 : index.ExactTotalCount % 50);
      clampedPage.Events.Select(logEvent => logEvent.Message).Should().Equal(
        Enumerable.Range(1, 50).Select(index => $"archive-{index}")
          .Take(clampedPage.Events.Count)
          .Reverse());
      clampedPage.HasNextPage.Should().BeFalse();
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public void A_page_matrix_runs_against_the_real_settings_window()
  {
    WpfStaTestHost.Run(() =>
    {
      var installRoot = Path.Combine(
        Path.GetTempPath(),
        "sg-log-pagination-window-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(installRoot);
      Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
      SettingsWindowController? controller = null;

      try
      {
        WriteWindowConfig(installRoot);
        WriteWindowLog(installRoot, eventCount: 1248);
        var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
        var repository = new GuardConfigRepository(configPath);
        var config = repository.LoadOrDefault(installRoot);
        controller = SettingsWindowController.TryCreate(installRoot, repository, config)
          ?? throw new InvalidOperationException("Failed to create the real Settings window.");
        WpfStaTestHost.Track(controller);
        var window = GetWindow(controller);
        WpfStaTestHost.ShowAndWait(window);
        window.Width = 1575;
        window.Height = 1000;
        window.UpdateLayout();
        controller.NavigateTo("logs");

        var table = (DataGrid)window.FindName("logTable")!;
        var totalLabel = (TextBlock)window.FindName("lblLogTotalCount")!;
        var statusLabel = (TextBlock)window.FindName("lblLogStatus")!;
        var pageSize = (ComboBox)window.FindName("cmbLogPageSize")!;
        var pageItems = (ItemsControl)window.FindName("logPageNumberItems")!;
        var coordinator = GetPaginationCoordinator(controller);

        WpfStaTestHost.WaitUntil(
          () => totalLabel.Text == "共 1,248 条日志"
             && table.Items.Count == 20
             && !coordinator.State.IsBusy
             && statusLabel.Text != "正在统计匹配日志…",
          TimeSpan.FromSeconds(10),
          "the first real paged table to show 20 rows and an exact total");
        statusLabel.Text.Should().NotContain("正在统计匹配日志");
        coordinator.State.TotalPages.Should().Be(63);
        pageSize.SelectedIndex.Should().Be(0);

        ExecutePage(window, 2);
        WpfStaTestHost.WaitUntil(
          () => coordinator.State.CurrentPage == 2
             && !coordinator.State.IsBusy
             && coordinator.State.ExactTotalCount == 1248
             && table.Items.Count == 20,
          TimeSpan.FromSeconds(10),
          "page 2 to apply");

        ExecutePage(window, 63);
        WpfStaTestHost.WaitUntil(
          () => coordinator.State.CurrentPage == 63
             && !coordinator.State.IsBusy
             && coordinator.State.ExactTotalCount == 1248
             && table.Items.Count == 8,
          TimeSpan.FromSeconds(10),
          "the last page to apply");
        coordinator.State.CanGoNext.Should().BeFalse();
        coordinator.State.CanGoLast.Should().BeFalse();

        pageSize.SelectedIndex = 1;
        WpfStaTestHost.WaitUntil(
          () => coordinator.State.PageSize == 50
             && coordinator.State.TotalPages == 25
             && coordinator.State.CurrentPage == 1
             && !coordinator.State.IsBusy
             && coordinator.State.ExactTotalCount == 1248,
          TimeSpan.FromSeconds(10),
          "page size 50 to reset and recalculate");
        pageSize.SelectedIndex = 2;
        WpfStaTestHost.WaitUntil(
          () => coordinator.State.PageSize == 100
             && coordinator.State.TotalPages == 13
             && coordinator.State.CurrentPage == 1
             && !coordinator.State.IsBusy
             && coordinator.State.ExactTotalCount == 1248,
          TimeSpan.FromSeconds(10),
          "page size 100 to reset and recalculate");

        pageSize.SelectedIndex = 0;
        WpfStaTestHost.WaitUntil(
          () => coordinator.State.PageSize == 20
             && coordinator.State.TotalPages == 63
             && coordinator.State.CurrentPage == 1
             && !coordinator.State.IsBusy
             && coordinator.State.ExactTotalCount == 1248,
          TimeSpan.FromSeconds(10),
          "page size 20 to restore");
        ExecutePage(window, 31);
        WpfStaTestHost.WaitUntil(
          () => coordinator.State.CurrentPage == 31
             && !coordinator.State.IsBusy
             && coordinator.State.ExactTotalCount == 1248,
          TimeSpan.FromSeconds(10),
          "the middle page to apply");

        var tokens = pageItems.Items
          .Cast<object>()
          .Select(item => item is Button button ? button.Content?.ToString() ?? string.Empty : "…")
          .ToArray();
        tokens.Should().Equal("1", "…", "30", "31", "32", "…", "63");

        WriteRealWindowScreenshot(window);
        WriteRealWindowEvidence(new
        {
          scenario = "A-PAGE-01..05",
          total = totalLabel.Text,
          pageSize = coordinator.State.PageSize,
          currentPage = coordinator.State.CurrentPage,
          totalPages = coordinator.State.TotalPages,
          rowsOnPage = table.Items.Count,
          tokens,
          nextEnabled = coordinator.State.CanGoNext,
          lastEnabled = coordinator.State.CanGoLast,
        });
      }
      finally
      {
        controller?.Dispose();
        TryDelete(installRoot);
      }
    });
  }

  private static void ExecutePage(Window window, int pageNumber)
    => LogPaginationCommands.GoToPage.Execute(pageNumber, window);

  private static SettingsLogPaginationCoordinator GetPaginationCoordinator(
    SettingsWindowController controller)
  {
    var host = GetLogPageHost(controller);
    var property = host.GetType().GetProperty(
      "PaginationCoordinatorForTests",
      BindingFlags.NonPublic | BindingFlags.Instance);
    return (SettingsLogPaginationCoordinator)property!.GetValue(host)!;
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

  private static void WriteWindowConfig(string installRoot)
  {
    File.WriteAllText(
      Path.Combine(installRoot, "SmartGuard.config.json"),
      "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000,\"ThemeFollowSystem\":false,\"ThemeIsDark\":false}",
      Utf8NoBom);
  }

  private static void WriteWindowLog(string installRoot, int eventCount)
  {
    using var writer = new StreamWriter(
      Path.Combine(installRoot, "SmartGuard.log"),
      append: false,
      Utf8NoBom);
    var start = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
    for (var i = 0; i < eventCount; i++)
    {
      var timestamp = start.AddSeconds(i);
      writer.WriteLine($"[INFO] {timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss} [Engine] window-page-{i + 1:0000}");
    }
  }

  private static void WriteRealWindowEvidence(object evidence)
  {
    var evidenceRoot = Path.Combine(SettingsSourcePaths.RepoRoot(), ".work", "log-pagination-acceptance");
    Directory.CreateDirectory(evidenceRoot);
    File.WriteAllText(
      Path.Combine(evidenceRoot, "real-window-matrix.json"),
      JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
      Utf8NoBom);
  }

  private static void WriteRealWindowScreenshot(Window window)
  {
    var visual = window.Content as FrameworkElement ?? window;
    visual.UpdateLayout();
    var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
    var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);

    var evidenceRoot = Path.Combine(SettingsSourcePaths.RepoRoot(), ".work", "log-pagination-acceptance");
    Directory.CreateDirectory(evidenceRoot);
    using var stream = File.Create(Path.Combine(evidenceRoot, "real-window.png"));
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    encoder.Save(stream);
  }

  private static void TryDelete(string path)
  {
    try { Directory.Delete(path, recursive: true); } catch { }
  }

  private sealed class LogTailReaderEventSource : ILogEventSource
  {
    private readonly LogTailReader _reader;

    internal LogTailReaderEventSource(LogTailReader reader) => _reader = reader;

    public Task<LogEventPage> ReadLatestAsync(LogReadRequest request, CancellationToken cancellationToken)
      => _reader.ReadLatestAsync(request, cancellationToken);

    public Task<LogEventPage> ReadOlderAsync(LogReadRequest request, CancellationToken cancellationToken)
      => _reader.ReadOlderAsync(request, cancellationToken);

    public Task<LogEventPage> ReadNewerAsync(LogReadRequest request, CancellationToken cancellationToken)
      => _reader.ReadNewerAsync(request, cancellationToken);

    public async IAsyncEnumerable<LogEvent> StreamAsync(
      LogReadScope scope,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
      LogReadCursor? cursor = null;
      while (true)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var page = cursor is null
          ? await _reader.ReadLatestAsync(
            new LogReadRequest(null, LogReadDirection.Latest, 500, LogTailReader.DefaultMaxBytes),
            cancellationToken).ConfigureAwait(false)
          : await _reader.ReadOlderAsync(
            new LogReadRequest(cursor, LogReadDirection.Older, 500, LogTailReader.DefaultMaxBytes),
            cancellationToken).ConfigureAwait(false);

        if (page.Events.Count == 0)
          yield break;

        foreach (var logEvent in page.Events)
          yield return logEvent;

        if (!page.HasOlder)
          yield break;

        cursor = page.OlderCursor;
      }
    }
  }
}
