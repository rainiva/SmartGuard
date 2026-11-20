using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "Performance")]
public class LogWorkbenchPerformanceTests
{
  private const int ArchiveCount = 30;
  private const int EventsPerArchive = 2000;
  private const int CurrentLogTargetBytes = 10 * 1024 * 1024;
  private const int WarmRuns = 20;
  private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

  [Fact]
  public void Ten_thousand_appends_keep_window_bounded_without_collection_reset()
  {
    var window = new LogEventWindow();
    window.Initialize([CreateEvent("e:seed")]);

    var resetCount = 0;
    var items = new ObservableCollection<string>();
    items.CollectionChanged += (_, args) =>
    {
      if (args.Action == NotifyCollectionChangedAction.Reset)
        resetCount++;
    };

    for (var i = 0; i < 10_000; i++)
      window.Append([CreateEvent($"e:{i}")]);

    window.Count.Should().BeLessThanOrEqualTo(LogEventWindow.MaxEventCount);
    window.EstimatedPayloadBytes.Should().BeLessThanOrEqualTo(LogEventWindow.MaxPayloadBytes);
    resetCount.Should().Be(0);
  }

  [Fact]
  public void Paged_pipeline_preserves_provisional_then_exact_progress_order()
  {
    var path = Path.Combine(
      SettingsSourcePaths.RepoRoot(),
      "src",
      "SmartGuard.Settings",
      "SettingsLogRefreshPipeline.cs");
    var source = File.ReadAllText(path, Encoding.UTF8);

    source.Should().Contain("new OrderedProgress<LogPaginationApplyModel>");
    source.Should().NotContain("new Progress<LogPaginationApplyModel>",
      "Progress<T> dispatches through the thread pool here and can apply the provisional model after the exact model");
  }

  [Fact]
  public void Log_viewport_scroll_handlers_do_not_touch_disk_or_decode_logs()
  {
    var source = File.ReadAllText(Path.Combine(
      SettingsSourcePaths.RepoRoot(),
      "src",
      "SmartGuard.Settings",
      "LogViewportController.cs"));

    var handlerBodies = ExtractMethodBodies(source, "OnPreviewMouseWheel", "OnScrollBarScroll", "OnScrollChanged");
    foreach (var body in handlerBodies)
    {
      body.Should().NotContain("File.", "scroll handlers must not perform disk I/O");
      body.Should().NotContain("LogTailReader", "scroll handlers must not read logs");
      body.Should().NotContain("LogEventDecoder", "scroll handlers must not decode logs");
      body.Should().NotContain("JsonSerializer", "scroll handlers must not parse JSON");
      body.Should().NotContain("LogQueryService", "scroll handlers must not run queries");
      body.Should().NotContain(".Clear(", "scroll handlers must not reset collections");
    }
  }

  [Fact]
  public async Task ReadLatest_on_large_fixture_returns_last_200_events_with_has_older()
  {
    var fixture = LogWorkbenchPerformanceFixture.Create(
      ArchiveCount,
      EventsPerArchive,
      CurrentLogTargetBytes);

    try
    {
      var reader = new LogTailReader(fixture.LogPath);
      var page = await reader.ReadLatestAsync(
        new LogReadRequest(null, LogReadDirection.Latest, 200, LogTailReader.DefaultMaxBytes),
        CancellationToken.None);

      page.Events.Should().HaveCount(200);
      page.HasOlder.Should().BeTrue();
      page.Events.Last().Message.Should().Contain("current-tail");
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public async Task ReadOlder_on_large_fixture_pages_without_duplicates()
  {
    var fixture = LogWorkbenchPerformanceFixture.Create(
      ArchiveCount,
      EventsPerArchive,
      CurrentLogTargetBytes);

    try
    {
      var reader = new LogTailReader(fixture.LogPath);
      var latest = await reader.ReadLatestAsync(
        new LogReadRequest(null, LogReadDirection.Latest, 200, LogTailReader.DefaultMaxBytes),
        CancellationToken.None);
      var older = await reader.ReadOlderAsync(
        new LogReadRequest(latest.OlderCursor, LogReadDirection.Older, 200, LogTailReader.DefaultMaxBytes),
        CancellationToken.None);

      latest.Events.Select(e => e.EventId)
        .Concat(older.Events.Select(e => e.EventId))
        .Should()
        .OnlyHaveUniqueItems();
      older.Events.Should().HaveCount(200);
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public async Task ReadNewer_on_large_fixture_skips_incomplete_half_line_until_completed()
  {
    var fixture = LogWorkbenchPerformanceFixture.Create(
      archiveCount: 0,
      eventsPerArchive: 0,
      currentLogTargetBytes: 4096,
      includeHalfLine: true);

    try
    {
      var reader = new LogTailReader(fixture.LogPath);
      var initial = await reader.ReadLatestAsync(
        new LogReadRequest(null, LogReadDirection.Latest, 200, LogTailReader.DefaultMaxBytes),
        CancellationToken.None);

      initial.Events.Should().NotContain(e => e.Message.Contains("half-line-marker"));

      await File.AppendAllTextAsync(fixture.LogPath, "-completed\n", Encoding.UTF8);
      var newer = await reader.ReadNewerAsync(
        new LogReadRequest(initial.NewerCursor, LogReadDirection.Newer, 200, LogTailReader.DefaultMaxBytes),
        CancellationToken.None);

      newer.Events.Should().ContainSingle(e => e.Message.Contains("half-line-marker-completed"));
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public void ReadLatest_records_p95_within_p0_budget_on_large_fixture()
  {
    var fixture = LogWorkbenchPerformanceFixture.Create(
      ArchiveCount,
      EventsPerArchive,
      CurrentLogTargetBytes);

    try
    {
      var sample = SampleAsync(async () =>
      {
        var reader = new LogTailReader(fixture.LogPath);
        _ = await reader.ReadLatestAsync(
          new LogReadRequest(null, LogReadDirection.Latest, 200, LogTailReader.DefaultMaxBytes),
          CancellationToken.None);
      });

      WritePerformanceSample("ReadLatest", sample);
      sample.P95Milliseconds.Should().BeLessThan(500,
        $"ReadLatest p95={sample.P95Milliseconds}ms max={sample.MaxMilliseconds}ms on {sample.MachineName}");
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public void ReadOlder_records_p95_within_p0_budget_on_large_fixture()
  {
    var fixture = LogWorkbenchPerformanceFixture.Create(
      ArchiveCount,
      EventsPerArchive,
      CurrentLogTargetBytes);

    try
    {
      var reader = new LogTailReader(fixture.LogPath);
      var latest = reader.ReadLatestAsync(
        new LogReadRequest(null, LogReadDirection.Latest, 200, LogTailReader.DefaultMaxBytes),
        CancellationToken.None).GetAwaiter().GetResult();

      var sample = SampleAsync(async () =>
      {
        var pageReader = new LogTailReader(fixture.LogPath);
        _ = await pageReader.ReadOlderAsync(
          new LogReadRequest(latest.OlderCursor, LogReadDirection.Older, 200, LogTailReader.DefaultMaxBytes),
          CancellationToken.None);
      });

      WritePerformanceSample("ReadOlder", sample);
      sample.P95Milliseconds.Should().BeLessThan(300,
        $"ReadOlder p95={sample.P95Milliseconds}ms max={sample.MaxMilliseconds}ms on {sample.MachineName}");
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public void ReadNewer_records_p95_within_p0_budget_on_large_fixture()
  {
    var fixture = LogWorkbenchPerformanceFixture.Create(
      archiveCount: 0,
      eventsPerArchive: 0,
      currentLogTargetBytes: 256 * 1024);

    try
    {
      var reader = new LogTailReader(fixture.LogPath);
      var initial = reader.ReadLatestAsync(
        new LogReadRequest(null, LogReadDirection.Latest, 200, LogTailReader.DefaultMaxBytes),
        CancellationToken.None).GetAwaiter().GetResult();

      File.AppendAllText(fixture.LogPath, LegacyLine(DateTime.UtcNow, "newer-tail") + "\n", Encoding.UTF8);

      var sample = SampleAsync(async () =>
      {
        var pageReader = new LogTailReader(fixture.LogPath);
        _ = await pageReader.ReadNewerAsync(
          new LogReadRequest(initial.NewerCursor, LogReadDirection.Newer, 200, LogTailReader.DefaultMaxBytes),
          CancellationToken.None);
      });

      WritePerformanceSample("ReadNewer", sample);
      sample.P95Milliseconds.Should().BeLessThan(300,
        $"ReadNewer p95={sample.P95Milliseconds}ms max={sample.MaxMilliseconds}ms on {sample.MachineName}");
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public void Preview_page_one_p95_is_within_500ms_on_fixed_fixture()
  {
    var fixture = LogWorkbenchPerformanceFixture.Create(
      ArchiveCount,
      EventsPerArchive,
      CurrentLogTargetBytes);

    try
    {
      var reader = new LogTailReader(fixture.LogPath);
      var snapshot = reader.CaptureSnapshot();
      var service = new LogQueryService();
      var expression = LogQueryMatchAllExpression.Instance;
      var scope = new LogReadScope(Array.Empty<LogSourceIdentity>());
      var sample = SampleAsync(() => service.ReadPreviewPageAsync(
        reader,
        expression,
        scope,
        snapshot,
        pageSize: 20,
        ignoreCase: true,
        CancellationToken.None));

      WritePagingPerformanceSample("PreviewPageOne", sample, scannedCount: null, anchorCount: null);
      sample.P95Milliseconds.Should().BeLessThan(
        500,
        $"Preview page p95={sample.P95Milliseconds}ms max={sample.MaxMilliseconds}ms on {sample.MachineName}");
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public async Task Indexed_random_page_p95_is_within_250ms_on_fixed_fixture()
  {
    var fixture = LogWorkbenchPerformanceFixture.Create(
      ArchiveCount,
      EventsPerArchive,
      CurrentLogTargetBytes);

    try
    {
      var reader = new LogTailReader(fixture.LogPath);
      var service = new LogQueryService();
      var snapshot = reader.CaptureSnapshot();
      var scope = new LogReadScope(Array.Empty<LogSourceIdentity>());
      var index = await service.BuildPageIndexAsync(
        reader,
        LogQueryMatchAllExpression.Instance,
        scope,
        snapshot,
        ignoreCase: true,
        progress: null,
        CancellationToken.None);
      var totalPages = Math.Max(1, (index.ExactTotalCount + 99) / 100);
      var random = new Random(20260808);
      var sample = SampleAsync(() => service.ReadPageAsync(
        reader,
        index,
        new LogQueryPageRequest(
          random.Next(1, totalPages + 1),
          PageSize: 100,
          LogSortDirection.NewestFirst),
        CancellationToken.None));

      WritePagingPerformanceSample(
        "IndexedRandomPage",
        sample,
        index.ScannedCount,
        index.Anchors.Count);
      sample.P95Milliseconds.Should().BeLessThan(
        250,
        $"Indexed random page p95={sample.P95Milliseconds}ms max={sample.MaxMilliseconds}ms on {sample.MachineName}");
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public async Task Page_index_anchor_count_is_bounded_by_match_count_over_256()
  {
    var fixture = LogWorkbenchPerformanceFixture.Create(
      ArchiveCount,
      EventsPerArchive,
      CurrentLogTargetBytes);

    try
    {
      var reader = new LogTailReader(fixture.LogPath);
      var service = new LogQueryService();
      var index = await service.BuildPageIndexAsync(
        reader,
        LogQueryMatchAllExpression.Instance,
        new LogReadScope(Array.Empty<LogSourceIdentity>()),
        reader.CaptureSnapshot(),
        ignoreCase: true,
        progress: null,
        CancellationToken.None);
      var anchorUpperBound = (index.ExactTotalCount + 255) / 256;

      index.Anchors.Count.Should().BeLessThanOrEqualTo(anchorUpperBound);
      WritePagingPerformanceSample(
        "PageIndexAnchors",
        sample: null,
        index.ScannedCount,
        index.Anchors.Count);
    }
    finally
    {
      fixture.Dispose();
    }
  }

  [Fact]
  public void Fifty_rapid_page_and_filter_changes_apply_latest_generation_only()
  {
    WpfStaTestHost.Run(() =>
    {
      var root = Path.Combine(Path.GetTempPath(), "sg-log-paging-generation-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(root);
      using var firstFlightStarted = new ManualResetEventSlim();
      using var releaseFirstFlight = new ManualResetEventSlim();
      var applied = new List<LogPaginationApplyModel>();
      var executionCount = 0;
      var window = new Window { Width = 320, Height = 80 };
      var controller = new LogViewController(
        new EmptyLogEventSource(),
        Path.Combine(root, "SmartGuard.log"));
      var pipeline = new SettingsLogRefreshPipeline(
        window,
        isDisposed: () => false,
        isLogPageActive: () => true,
        getRefreshableController: () => controller,
        applyFilters: _ => { },
        showFailure: _ => { },
        applyRefreshResult: (_, _) => { },
        applyScrollIfEnabled: () => { });
      pipeline.ConfigurePagedApply(model => applied.Add(model));

      try
      {
        WpfStaTestHost.ShowAndWait(window);
        var expression = LogQueryMatchAllExpression.Instance;
        var scope = new LogReadScope(Array.Empty<LogSourceIdentity>());
        var initialIntent = new LogPaginationIntent(
          QueryGeneration: 0,
          expression,
          scope,
          PageNumber: 1,
          PageSize: 20,
          LogSortDirection.NewestFirst,
          IgnoreCase: true,
          CaptureNewSnapshot: true);

        pipeline.SchedulePaged(
          initialIntent,
          async (intent, progress, cancellationToken) =>
          {
            if (Interlocked.Increment(ref executionCount) == 1)
            {
              firstFlightStarted.Set();
              await Task.Run(() => releaseFirstFlight.Wait(cancellationToken), cancellationToken);
            }

            progress.Report(CreateGenerationModel(intent));
          });

        WpfStaTestHost.WaitUntil(
          () => firstFlightStarted.IsSet,
          TimeSpan.FromSeconds(5),
          "the first paged flight to start");

        for (var i = 0; i < 50; i++)
        {
          var page = (i % 9) + 1;
          pipeline.SchedulePaged(
            initialIntent with
            {
              PageNumber = page,
              PageSize = i == 49 ? 20 : i % 2 == 0 ? 50 : 100,
              CaptureNewSnapshot = false,
            },
            (intent, progress, _) =>
            {
              progress.Report(CreateGenerationModel(intent));
              return Task.CompletedTask;
            });
        }

        releaseFirstFlight.Set();
        WpfStaTestHost.WaitUntil(
          () => !pipeline.IsRefreshInFlight,
          TimeSpan.FromSeconds(10),
          "the initial and latest paged flights to settle");
        WpfStaTestHost.PumpDispatcher(window);

        applied.Select(model => model.QueryGeneration).Should().Equal(
          [1, 51],
          "the active flight may publish usable data before the final coalesced request, but intermediate generations must never overwrite either result");
        applied[^1].PageNumber.Should().Be(5);
        applied[^1].PageSize.Should().Be(20);
      }
      finally
      {
        releaseFirstFlight.Set();
        pipeline.Shutdown();
        window.Close();
        TryDelete(root);
      }
    });
  }

  private static LogPaginationApplyModel CreateGenerationModel(LogPaginationIntent intent)
    => new(
      intent.QueryGeneration,
      IsProvisional: false,
      Events: Array.Empty<LogEvent>(),
      intent.PageNumber,
      intent.PageSize,
      ExactTotalCount: 100,
      IsScanComplete: true,
      intent.SortDirection,
      new LogSourceSnapshot(Array.Empty<LogSourceSnapshotEntry>(), "rapid-generation"),
      Index: null,
      ErrorMessage: null);

  private static LogEvent CreateEvent(string eventId, string message = "payload")
    => new(
      LogEvent.CurrentSchemaVersion,
      eventId,
      DateTimeOffset.UtcNow,
      LogSeverity.Info,
      LogComponentRegistry.Legacy,
      LogChannelRegistry.Compatibility,
      EventName: null,
      message,
      Exception: null,
      OperationId: null,
      TraceId: null,
      SpanId: null,
      UserActionId: null,
      ProcessId: null,
      ThreadId: null,
      LogEvent.EmptyProperties);

  private static string LegacyLine(DateTimeOffset timestamp, string message)
    => $"[INFO] {timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss} [Engine] {message}";

  private static IEnumerable<string> ExtractMethodBodies(string source, params string[] methodNames)
  {
    foreach (var methodName in methodNames)
    {
      var pattern = $@"private void {methodName}\([^{{]*\{{";
      var match = System.Text.RegularExpressions.Regex.Match(
        source,
        pattern,
        System.Text.RegularExpressions.RegexOptions.Singleline);
      match.Success.Should().BeTrue($"expected method {methodName} in LogViewportController");
      var start = match.Index + match.Length;
      var depth = 1;
      var index = start;
      while (index < source.Length && depth > 0)
      {
        if (source[index] == '{')
          depth++;
        else if (source[index] == '}')
          depth--;
        index++;
      }

      yield return source[start..(index - 1)];
    }
  }

  private static PerformanceSampleRecord SampleAsync(Func<Task> action)
  {
    GCSettings.LatencyMode = GCLatencyMode.Batch;
    action().GetAwaiter().GetResult();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var process = Process.GetCurrentProcess();
    var workingSetBefore = process.WorkingSet64;
    var samples = new List<long>(WarmRuns);
    for (var i = 0; i < WarmRuns; i++)
    {
      var sw = Stopwatch.StartNew();
      action().GetAwaiter().GetResult();
      sw.Stop();
      samples.Add(sw.ElapsedMilliseconds);
    }

    process.Refresh();
    samples.Sort();
    return new PerformanceSampleRecord(
      Environment.MachineName,
      RuntimeInformation.OSDescription,
      RuntimeInformation.ProcessArchitecture.ToString(),
      samples.Count,
      Percentile(samples, 0.50),
      Percentile(samples, 0.95),
      samples[^1],
      process.WorkingSet64 - workingSetBefore);
  }

  private static long Percentile(IReadOnlyList<long> sortedSamples, double percentile)
  {
    if (sortedSamples.Count == 0)
      return 0;

    var index = (int)Math.Ceiling(percentile * sortedSamples.Count) - 1;
    index = Math.Clamp(index, 0, sortedSamples.Count - 1);
    return sortedSamples[index];
  }

  private static void WritePerformanceSample(string scenario, PerformanceSampleRecord sample)
  {
    var evidenceRoot = Path.Combine(
      SettingsSourcePaths.RepoRoot(),
      ".work",
      "log-workbench-execution",
      "20260802T104839Z",
      "P0-08");
    Directory.CreateDirectory(evidenceRoot);
    var path = Path.Combine(evidenceRoot, "performance-results.json");
    List<object> records;
    if (File.Exists(path))
    {
      var existing = File.ReadAllText(path, Encoding.UTF8);
      records = JsonSerializer.Deserialize<List<object>>(existing) ?? [];
    }
    else
    {
      records = [];
    }

    records.Add(new
    {
      scenario,
      recordedAtUtc = DateTime.UtcNow.ToString("O"),
      sample.MachineName,
      sample.OsDescription,
      sample.Architecture,
      sample.WarmRuns,
      p50Ms = sample.P50Milliseconds,
      p95Ms = sample.P95Milliseconds,
      maxMs = sample.MaxMilliseconds,
      workingSetDeltaBytes = sample.WorkingSetDeltaBytes,
    });

    File.WriteAllText(path, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
  }

  private static void WritePagingPerformanceSample(
    string scenario,
    PerformanceSampleRecord? sample,
    int? scannedCount,
    int? anchorCount)
  {
    var evidenceRoot = Path.Combine(
      SettingsSourcePaths.RepoRoot(),
      ".work",
      "log-pagination-acceptance");
    Directory.CreateDirectory(evidenceRoot);
    var path = Path.Combine(evidenceRoot, "performance-results.jsonl");
    var record = new
    {
      scenario,
      recordedAtUtc = DateTime.UtcNow.ToString("O"),
      machineName = sample?.MachineName ?? Environment.MachineName,
      p50Ms = sample?.P50Milliseconds,
      p95Ms = sample?.P95Milliseconds,
      maxMs = sample?.MaxMilliseconds,
      workingSetDeltaBytes = sample?.WorkingSetDeltaBytes,
      scannedCount,
      anchorCount,
    };
    File.AppendAllText(
      path,
      JsonSerializer.Serialize(record) + Environment.NewLine,
      Utf8NoBom);
  }

  private static void TryDelete(string path)
  {
    try { Directory.Delete(path, recursive: true); } catch { }
  }

  private sealed class EmptyLogEventSource : ILogEventSource
  {
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
      await Task.CompletedTask;
      yield break;
    }
  }

  private sealed record PerformanceSampleRecord(
    string MachineName,
    string OsDescription,
    string Architecture,
    int WarmRuns,
    long P50Milliseconds,
    long P95Milliseconds,
    long MaxMilliseconds,
    long WorkingSetDeltaBytes);
}

internal sealed class LogWorkbenchPerformanceFixture : IDisposable
{
  private readonly string _root;

  private LogWorkbenchPerformanceFixture(string root, string logPath, int currentEventCount)
  {
    _root = root;
    LogPath = logPath;
    CurrentEventCount = currentEventCount;
  }

  internal string LogPath { get; }

  internal int CurrentEventCount { get; }

  internal static LogWorkbenchPerformanceFixture Create(
    int archiveCount,
    int eventsPerArchive,
    int currentLogTargetBytes,
    bool includeHalfLine = false)
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-log-perf-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var logPath = Path.Combine(root, "SmartGuard.log");

    for (var archiveIndex = 0; archiveIndex < archiveCount; archiveIndex++)
    {
      var date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(archiveIndex);
      var archivePath = logPath + $".{date:yyyyMMdd}.bak";
      WriteLegacyEvents(archivePath, eventsPerArchive, date, archiveIndex * eventsPerArchive);
    }

    var currentEventCount = WriteCurrentLog(logPath, currentLogTargetBytes, includeHalfLine);
    return new LogWorkbenchPerformanceFixture(root, logPath, currentEventCount);
  }

  public void Dispose()
  {
    try { Directory.Delete(_root, recursive: true); } catch { }
  }

  private static void WriteLegacyEvents(string path, int count, DateTime dayUtc, int sequenceOffset)
  {
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    using var writer = new StreamWriter(stream, Encoding.UTF8);
    for (var i = 0; i < count; i++)
    {
      var timestamp = dayUtc.AddSeconds(i);
      writer.Write(LegacyLine(timestamp, $"archive-{sequenceOffset + i}"));
      writer.Write('\n');
    }
  }

  private static int WriteCurrentLog(string path, int targetBytes, bool includeHalfLine)
  {
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    using var writer = new StreamWriter(stream, Encoding.UTF8);
    var index = 0;
    while (stream.Length < targetBytes)
    {
      var timestamp = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero).AddSeconds(index);
      var marker = $"current-{index}";
      writer.Write(LegacyLine(timestamp, marker));
      writer.Write('\n');
      index++;
    }

    if (index > 0)
    {
      var tailTimestamp = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero).AddSeconds(index);
      writer.Write(LegacyLine(tailTimestamp, "current-tail"));
      writer.Write('\n');
    }

    if (includeHalfLine)
      writer.Write("[INFO] 2026-08-01 12:00:00 [Engine] half-line-marker");

    return index + (index > 0 ? 1 : 0);
  }

  private static string LegacyLine(DateTimeOffset timestamp, string message)
    => $"[INFO] {timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss} [Engine] {message}";
}
