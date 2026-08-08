using System.Globalization;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public sealed class LogPagedQueryServiceTests
{
  private sealed record Enumeration(
    LogReadScope Scope,
    LogSourceSnapshot Snapshot,
    LogReadDirection Direction,
    LogReadCursor? Cursor);

  private sealed class SnapshotLineSource : ILogEventSource, ILogSnapshotPhysicalLineSource
  {
    private readonly IReadOnlyList<LogPhysicalLine> _lines;
    private readonly LogSourceSnapshot _snapshot;

    public SnapshotLineSource(
      IReadOnlyList<LogPhysicalLine> lines,
      LogSourceSnapshot snapshot)
    {
      _lines = lines;
      _snapshot = snapshot;
    }

    public List<Enumeration> Enumerations { get; } = [];

    public int CaptureSnapshotCalls { get; private set; }

    public int EnumeratedLineCount { get; private set; }

    public Action<LogPhysicalLine>? BeforeYield { get; set; }

    public void ResetMetrics()
    {
      Enumerations.Clear();
      EnumeratedLineCount = 0;
    }

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
      return _snapshot;
    }

    public IEnumerable<LogPhysicalLine> EnumeratePhysicalLines(
      LogReadScope scope,
      LogSourceSnapshot snapshot,
      LogReadDirection direction,
      LogReadCursor? cursor,
      CancellationToken cancellationToken)
    {
      Enumerations.Add(new Enumeration(scope, snapshot, direction, cursor));

      var sourceFilter = scope.Sources.Count == 0
        ? null
        : scope.Sources.ToHashSet();
      var cursorIndex = cursor is null
        ? 0
        : _lines.ToList().FindIndex(line =>
          line.Source.Equals(cursor.Source) && line.ByteOffset == cursor.ByteOffset) + 1;
      if (cursor is not null && cursorIndex == 0)
        throw new LogSnapshotInvalidatedException("The test cursor source is unavailable.");

      var capturedLengthBySource = snapshot.Sources
        .ToDictionary(entry => entry.Source, entry => entry.CapturedLength);
      var orderedLines = direction == LogReadDirection.Latest || direction == LogReadDirection.Older
        ? _lines.Reverse()
        : _lines;

      foreach (var line in orderedLines.Skip(direction == LogReadDirection.Newer ? cursorIndex : 0))
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceFilter is not null && !sourceFilter.Contains(line.Source))
          continue;
        if (!capturedLengthBySource.TryGetValue(line.Source, out var capturedLength)
            || line.ByteOffset >= capturedLength)
          continue;

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

  [Theory]
  [InlineData(1, 20, 1_248, 1_229)]
  [InlineData(2, 20, 1_228, 1_209)]
  [InlineData(63, 20, 8, 1)]
  public async Task NewestFirst_pages_match_exact_global_ordinals(
    int pageNumber,
    int pageSize,
    int expectedFirstOrdinal,
    int expectedLastOrdinal)
  {
    var fixture = CreateFixture();
    var index = await BuildIndexAsync(fixture);
    var service = new LogQueryService();

    var result = await service.ReadPageAsync(
      fixture.EventSource,
      index,
      new LogQueryPageRequest(pageNumber, pageSize, LogSortDirection.NewestFirst),
      CancellationToken.None);

    result.PageNumber.Should().Be(pageNumber);
    result.ExactTotalCount.Should().Be(1_248);
    result.TotalPages.Should().Be(63);
    Ordinals(result.Events).Should().Equal(
      Enumerable.Range(expectedLastOrdinal, expectedFirstOrdinal - expectedLastOrdinal + 1)
        .Reverse());
  }

  [Fact]
  public async Task Adjacent_pages_have_no_duplicates_or_gaps()
  {
    var fixture = CreateFixture();
    var index = await BuildIndexAsync(fixture);
    var service = new LogQueryService();
    var ordinals = new List<int>();

    for (var page = 1; page <= 63; page++)
    {
      var result = await service.ReadPageAsync(
        fixture.EventSource,
        index,
        new LogQueryPageRequest(page, 20, LogSortDirection.NewestFirst),
        CancellationToken.None);
      ordinals.AddRange(Ordinals(result.Events));
    }

    ordinals.Should().Equal(Enumerable.Range(1, 1_248).Reverse());
    ordinals.Should().OnlyHaveUniqueItems();
  }

  [Fact]
  public async Task Last_partial_page_and_out_of_range_page_are_exactly_clamped()
  {
    var fixture = CreateFixture();
    var index = await BuildIndexAsync(fixture);
    var service = new LogQueryService();

    var result = await service.ReadPageAsync(
      fixture.EventSource,
      index,
      new LogQueryPageRequest(10_000, 20, LogSortDirection.NewestFirst),
      CancellationToken.None);

    result.PageNumber.Should().Be(63);
    result.Events.Should().HaveCount(8);
    Ordinals(result.Events).Should().Equal(Enumerable.Range(1, 8).Reverse());
    result.HasPreviousPage.Should().BeTrue();
    result.HasNextPage.Should().BeFalse();
  }

  [Fact]
  public async Task OldestFirst_and_page_sizes_reuse_the_same_index_and_sparse_anchors()
  {
    var fixture = CreateFixture();
    var index = await BuildIndexAsync(fixture);
    var service = new LogQueryService();
    fixture.EventSource.Enumerations.Clear();

    var oldest = await service.ReadPageAsync(
      fixture.EventSource,
      index,
      new LogQueryPageRequest(1, 20, LogSortDirection.OldestFirst),
      CancellationToken.None);
    var page50 = await service.ReadPageAsync(
      fixture.EventSource,
      index,
      new LogQueryPageRequest(2, 50, LogSortDirection.OldestFirst),
      CancellationToken.None);
    fixture.EventSource.ResetMetrics();
    var page100 = await service.ReadPageAsync(
      fixture.EventSource,
      index,
      new LogQueryPageRequest(4, 100, LogSortDirection.OldestFirst),
      CancellationToken.None);

    Ordinals(oldest.Events).Should().Equal(Enumerable.Range(1, 20));
    Ordinals(page50.Events).Should().Equal(Enumerable.Range(51, 50));
    Ordinals(page100.Events).Should().Equal(Enumerable.Range(301, 100));
    fixture.EventSource.EnumeratedLineCount.Should().Be(144);
    fixture.EventSource.Enumerations.Should().OnlyContain(call =>
      ReferenceEquals(call.Snapshot, index.Snapshot)
      && ReferenceEquals(call.Scope, index.Scope));
    fixture.EventSource.Enumerations.Where(call => call.Cursor is not null)
      .Should().NotBeEmpty();
    fixture.EventSource.CaptureSnapshotCalls.Should().Be(0);
  }

  [Fact]
  public async Task Filtered_pages_use_ordinals_from_the_complete_filtered_snapshot()
  {
    var fixture = CreateFixture(
      messageFactory: ordinal => ordinal % 3 == 0 ? "keep" : "drop");
    var index = await BuildIndexAsync(fixture, "message:keep");
    var service = new LogQueryService();

    var result = await service.ReadPageAsync(
      fixture.EventSource,
      index,
      new LogQueryPageRequest(1, 20, LogSortDirection.NewestFirst),
      CancellationToken.None);

    result.ExactTotalCount.Should().Be(416);
    result.Events.Should().HaveCount(20);
    result.Events.Should().OnlyContain(logEvent => logEvent.Message == "keep");
  }

  [Fact]
  public async Task Page_read_never_returns_events_appended_after_snapshot()
  {
    var fixture = CreateFixture(appendedRows: 20);
    var index = await BuildIndexAsync(fixture);
    var service = new LogQueryService();

    var result = await service.ReadPageAsync(
      fixture.EventSource,
      index,
      new LogQueryPageRequest(1, 20, LogSortDirection.NewestFirst),
      CancellationToken.None);

    index.ExactTotalCount.Should().Be(1_248);
    Ordinals(result.Events).Should().OnlyContain(ordinal => ordinal <= 1_248);
  }

  [Fact]
  public async Task Page_read_rejects_incompatible_source_and_cancellation()
  {
    var fixture = CreateFixture();
    var index = await BuildIndexAsync(fixture);
    var service = new LogQueryService();

    Func<Task> incompatible = () => service.ReadPageAsync(
      new IncompatibleEventSource(),
      index,
      new LogQueryPageRequest(1, 20, LogSortDirection.NewestFirst),
      CancellationToken.None);
    await incompatible.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage($"*{nameof(ILogSnapshotPhysicalLineSource)}*");

    using var cancellation = new CancellationTokenSource();
    fixture.EventSource.BeforeYield = _ => cancellation.Cancel();
    Func<Task> cancelled = () => service.ReadPageAsync(
      fixture.EventSource,
      index,
      new LogQueryPageRequest(1, 20, LogSortDirection.NewestFirst),
      cancellation.Token);
    await cancelled.Should().ThrowAsync<OperationCanceledException>();
  }

  private static async Task<LogQueryPageIndex> BuildIndexAsync(
    Fixture fixture,
    string query = "message~row")
  {
    var expression = LogQueryParser.Parse(query).Expression!;
    return await new LogQueryService().BuildPageIndexAsync(
      fixture.EventSource,
      expression,
      fixture.Scope,
      fixture.Snapshot,
      ignoreCase: true,
      progress: null,
      CancellationToken.None);
  }

  private static IReadOnlyList<int> Ordinals(IReadOnlyList<LogEvent> events)
    => events.Select(logEvent => int.Parse(logEvent.Message[4..], CultureInfo.InvariantCulture)).ToArray();

  private static Fixture CreateFixture(
    int appendedRows = 0,
    Func<int, string>? messageFactory = null)
  {
    var source = LogSourceIdentity.FromContentPrefix(
      new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
      "paged-query-fixture");
    messageFactory ??= ordinal => $"row-{ordinal:D4}";
    var lines = Enumerable.Range(1, 1_248 + appendedRows)
      .Select(ordinal => LegacyLine(
        source,
        ordinal - 1,
        messageFactory(ordinal)))
      .ToArray();
    var snapshot = new LogSourceSnapshot(
      [new LogSourceSnapshotEntry(
        source,
        LogSourceKind.Current,
        CapturedLength: 1_248 * 128L,
        BoundaryFingerprint: "fixture-boundary")],
      Fingerprint: "fixture-snapshot");
    var scope = new LogReadScope([source]);
    return new Fixture(source, lines, snapshot, scope, new SnapshotLineSource(lines, snapshot));
  }

  private static LogPhysicalLine LegacyLine(
    LogSourceIdentity source,
    int ordinal,
    string message)
    => new(
      source,
      ordinal * 128L,
      $"[INFO] 2026-08-01 10:00:00 [Engine] {EventNameRegistry.AppStarted} {message}",
      IsComplete: true,
      Ordinal: ordinal);

  private sealed record Fixture(
    LogSourceIdentity Source,
    IReadOnlyList<LogPhysicalLine> Lines,
    LogSourceSnapshot Snapshot,
    LogReadScope Scope,
    SnapshotLineSource EventSource);
}
