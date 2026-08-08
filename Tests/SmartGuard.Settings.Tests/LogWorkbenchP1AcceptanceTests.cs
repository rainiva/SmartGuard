using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

public class LogWorkbenchP1AcceptanceTests
{
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
    var fixture = LogWorkbenchPerformanceFixture.Create(
      archiveCount: 30,
      eventsPerArchive: 2000,
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
      index.ExactTotalCount.Should().BeGreaterThan(30 * 2000);
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
