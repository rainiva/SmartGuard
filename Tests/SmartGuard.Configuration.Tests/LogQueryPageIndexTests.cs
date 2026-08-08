using System.Globalization;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public sealed class LogQueryPageIndexTests
{
  private sealed record SnapshotEnumeration(
    LogReadScope Scope,
    LogSourceSnapshot Snapshot,
    LogReadDirection Direction);

  private sealed class SnapshotLineSource : ILogEventSource, ILogSnapshotPhysicalLineSource
  {
    private readonly IReadOnlyList<LogPhysicalLine> _lines;

    public SnapshotLineSource(IReadOnlyList<LogPhysicalLine> lines)
    {
      _lines = lines;
      ReplacementSnapshot = CreateSnapshot(TestSource("replacement"));
    }

    public List<SnapshotEnumeration> Enumerations { get; } = [];

    public int CaptureSnapshotCalls { get; private set; }

    public int EnumeratedLineCount { get; private set; }

    public Action<LogPhysicalLine>? BeforeYield { get; set; }

    public LogSourceSnapshot ReplacementSnapshot { get; }

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
      return ReplacementSnapshot;
    }

    public IEnumerable<LogPhysicalLine> EnumeratePhysicalLines(
      LogReadScope scope,
      LogSourceSnapshot snapshot,
      LogReadDirection direction,
      LogReadCursor? cursor,
      CancellationToken cancellationToken)
    {
      Enumerations.Add(new SnapshotEnumeration(scope, snapshot, direction));

      var scopedLines = _lines.Where(line =>
        scope.Sources.Count == 0 || scope.Sources.Contains(line.Source));
      var orderedLines = direction == LogReadDirection.Latest
        ? scopedLines.Reverse()
        : scopedLines;

      foreach (var line in orderedLines)
      {
        cancellationToken.ThrowIfCancellationRequested();
        EnumeratedLineCount++;
        BeforeYield?.Invoke(line);
        yield return line;
      }
    }
  }

  private sealed class IncompatibleEventSource : ILogEventSource
  {
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
      => throw new InvalidOperationException("The unbounded event stream is not available.");
  }

  private sealed class CollectingProgress : IProgress<LogQueryProgress>
  {
    public List<LogQueryProgress> Reports { get; } = [];

    public void Report(LogQueryProgress value) => Reports.Add(value);
  }

  [Fact]
  public async Task BuildPageIndexAsync_counts_matches_and_keeps_sparse_256_match_anchors()
  {
    var source = TestSource("fixture");
    var lines = Enumerable.Range(0, 1250)
      .Select(index => index == 0
        ? LegacyLine(source, index, "unrelated")
        : LegacyLine(source, index, $"match-{index:D4}"))
      .ToArray();
    var eventSource = new SnapshotLineSource(lines);
    var snapshot = CreateSnapshot(source);
    var scope = new LogReadScope([source]);
    var expression = LogQueryParser.Parse("message~match").Expression!;

    var result = await new LogQueryService().BuildPageIndexAsync(
      eventSource,
      expression,
      scope,
      snapshot,
      ignoreCase: true,
      progress: null,
      CancellationToken.None);

    result.ExactTotalCount.Should().Be(1_249);
    result.ScannedCount.Should().Be(1_250);
    result.Anchors.Select(anchor => anchor.MatchOrdinal)
      .Should().Equal(256, 512, 768, 1024);

    foreach (var anchor in result.Anchors)
    {
      var line = lines.Single(candidate => candidate.Ordinal == anchor.MatchOrdinal);
      var decoded = LogEventDecoder.Decode(line);
      anchor.Cursor.Should().Be(new LogReadCursor(line.Source, line.ByteOffset, decoded.EventId));
    }
  }

  [Fact]
  public async Task ReadPreviewPageAsync_returns_newest_matches_up_to_page_size_without_full_scan()
  {
    var source = TestSource("preview");
    var lines = Enumerable.Range(0, 20)
      .Select(index => LegacyLine(
        source,
        index,
        index % 2 == 0 ? $"hit-{index:D2}" : $"miss-{index:D2}"))
      .ToArray();
    var eventSource = new SnapshotLineSource(lines);
    var snapshot = CreateSnapshot(source);
    var expression = LogQueryParser.Parse("message~hit").Expression!;

    var result = await new LogQueryService().ReadPreviewPageAsync(
      eventSource,
      expression,
      new LogReadScope([source]),
      snapshot,
      pageSize: 3,
      ignoreCase: true,
      CancellationToken.None);

    result.Should().HaveCount(3);
    result.Select(logEvent => logEvent.Message).Should().Equal("hit-18", "hit-16", "hit-14");
    eventSource.Enumerations.Should().ContainSingle();
    eventSource.Enumerations[0].Direction.Should().Be(LogReadDirection.Latest);
    eventSource.EnumeratedLineCount.Should().BeLessThan(lines.Length);
  }

  [Fact]
  public async Task BuildPageIndexAsync_reports_exact_total_only_on_final_progress()
  {
    var source = TestSource("progress");
    var lines = Enumerable.Range(0, 6)
      .Select(index => LegacyLine(source, index, index == 2 ? "other" : $"match-{index}"))
      .ToArray();
    var eventSource = new SnapshotLineSource(lines);
    var progress = new CollectingProgress();
    var expression = LogQueryParser.Parse("message~match").Expression!;

    await new LogQueryService().BuildPageIndexAsync(
      eventSource,
      expression,
      new LogReadScope([source]),
      CreateSnapshot(source),
      ignoreCase: true,
      progress,
      CancellationToken.None);

    progress.Reports.Should().NotBeEmpty();
    progress.Reports[^1].IsComplete.Should().BeTrue();
    progress.Reports[^1].ExactTotal.Should().Be(5);
    progress.Reports.Take(progress.Reports.Count - 1)
      .Should().OnlyContain(report => !report.IsComplete && report.ExactTotal == null);
  }

  [Fact]
  public async Task BuildPageIndexAsync_propagates_cancellation_without_publishing_partial_index()
  {
    var source = TestSource("cancel");
    var lines = Enumerable.Range(0, 100)
      .Select(index => LegacyLine(source, index, $"match-{index:D3}"))
      .ToArray();
    using var cancellation = new CancellationTokenSource();
    var eventSource = new SnapshotLineSource(lines)
    {
      BeforeYield = line =>
      {
        if (line.Ordinal == 9)
          cancellation.Cancel();
      },
    };
    var expression = LogQueryParser.Parse("message~match").Expression!;

    Func<Task> action = () => new LogQueryService().BuildPageIndexAsync(
      eventSource,
      expression,
      new LogReadScope([source]),
      CreateSnapshot(source),
      ignoreCase: true,
      progress: null,
      cancellation.Token);

    await action.Should().ThrowAsync<OperationCanceledException>();
  }

  [Fact]
  public async Task Snapshot_page_operations_reject_sources_without_snapshot_contract()
  {
    var source = TestSource("incompatible");
    var eventSource = new IncompatibleEventSource();
    var expression = LogQueryMatchAllExpression.Instance;
    var snapshot = CreateSnapshot(source);
    var scope = new LogReadScope([source]);
    var service = new LogQueryService();

    var previewError = await Assert.ThrowsAnyAsync<Exception>(() => service.ReadPreviewPageAsync(
      eventSource,
      expression,
      scope,
      snapshot,
      pageSize: 1,
      ignoreCase: true,
      CancellationToken.None));
    previewError.Message.Should().Contain(nameof(ILogSnapshotPhysicalLineSource));

    var indexError = await Assert.ThrowsAnyAsync<Exception>(() => service.BuildPageIndexAsync(
      eventSource,
      expression,
      scope,
      snapshot,
      ignoreCase: true,
      progress: null,
      CancellationToken.None));
    indexError.Message.Should().Contain(nameof(ILogSnapshotPhysicalLineSource));
  }

  [Fact]
  public async Task Snapshot_page_operations_use_caller_snapshot_without_capturing_replacement()
  {
    var source = TestSource("identity");
    var lines = new[] { LegacyLine(source, 0, "match") };
    var eventSource = new SnapshotLineSource(lines);
    var snapshot = CreateSnapshot(source);
    var scope = new LogReadScope([source]);
    var expression = LogQueryMatchAllExpression.Instance;
    var service = new LogQueryService();

    await service.ReadPreviewPageAsync(
      eventSource,
      expression,
      scope,
      snapshot,
      pageSize: 1,
      ignoreCase: true,
      CancellationToken.None);
    await service.BuildPageIndexAsync(
      eventSource,
      expression,
      scope,
      snapshot,
      ignoreCase: true,
      progress: null,
      CancellationToken.None);

    eventSource.CaptureSnapshotCalls.Should().Be(0);
    eventSource.Enumerations.Should().HaveCount(2);
    eventSource.Enumerations.Should().OnlyContain(call =>
      ReferenceEquals(call.Scope, scope)
      && ReferenceEquals(call.Snapshot, snapshot));
  }

  [Fact]
  public async Task BuildPageIndexAsync_applies_combined_expression_and_source_scope()
  {
    var includedSource = TestSource("included");
    var excludedSource = TestSource("excluded");
    var lines = new[]
    {
      LegacyLine(includedSource, 0, "timeout while starting", "ERROR", "Engine", "2026-08-01 10:00:00", "op-a", "trace-a"),
      LegacyLine(includedSource, 1, "timeout while starting", "ERROR", "Engine", "2026-08-01 10:00:01", "op-b", "trace-a"),
      LegacyLine(includedSource, 2, "timeout while starting", "INFO", "Engine", "2026-08-01 10:00:02", "op-a", "trace-a"),
      LegacyLine(includedSource, 3, "timeout while starting", "ERROR", "Tray", "2026-08-01 10:00:03", "op-a", "trace-a"),
      LegacyLine(includedSource, 4, "healthy", "ERROR", "Engine", "2026-08-01 10:00:04", "op-a", "trace-a"),
      LegacyLine(includedSource, 5, "timeout while starting", "ERROR", "Engine", "2026-07-31 10:00:05", "op-a", "trace-a"),
      LegacyLine(excludedSource, 6, "timeout while starting", "ERROR", "Engine", "2026-08-01 10:00:06", "op-a", "trace-a"),
    };
    var eventSource = new SnapshotLineSource(lines);
    var expression = LogQueryParser.Parse(
      "severity:ERROR AND component:Engine AND message~^timeout AND op:op-a AND trace:trace-a AND time>=2026-08-01T00:00:00Z")
      .Expression!;

    var result = await new LogQueryService().BuildPageIndexAsync(
      eventSource,
      expression,
      new LogReadScope([includedSource]),
      CreateSnapshot(includedSource, excludedSource),
      ignoreCase: true,
      progress: null,
      CancellationToken.None);

    result.ExactTotalCount.Should().Be(1);
    result.ScannedCount.Should().Be(6);
  }

  private static LogSourceIdentity TestSource(string suffix)
    => LogSourceIdentity.FromContentPrefix(
      new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
      $"page-index-{suffix}");

  private static LogSourceSnapshot CreateSnapshot(params LogSourceIdentity[] sources)
    => new(
      sources.Select(source => new LogSourceSnapshotEntry(
        source,
        LogSourceKind.Current,
        CapturedLength: 1_000_000,
        BoundaryFingerprint: $"boundary-{source.ContentHashHex}"))
        .ToArray(),
      Fingerprint: "caller-supplied-snapshot");

  private static LogPhysicalLine LegacyLine(
    LogSourceIdentity source,
    int ordinal,
    string message,
    string level = "INFO",
    string component = "Engine",
    string timestamp = "2026-08-01 10:00:00",
    string? operationId = null,
    string? traceId = null)
  {
    var remainder = $"{EventNameRegistry.AppStarted} {message}";
    if (operationId is not null)
      remainder += $" op={operationId}";
    if (traceId is not null)
      remainder += $" trace={traceId}";

    return new LogPhysicalLine(
      source,
      ordinal * 128L,
      $"[{level}] {DateTime.Parse(timestamp, CultureInfo.InvariantCulture):yyyy-MM-dd HH:mm:ss} [{component}] {remainder}",
      IsComplete: true,
      Ordinal: ordinal);
  }
}
