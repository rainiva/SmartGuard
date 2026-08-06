using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogAggregationServiceTests
{
  private static LogEvent CreateEvent(
    string eventId,
    DateTimeOffset timestamp,
    LogSeverity severity = LogSeverity.Info,
    string component = LogComponentRegistry.Engine,
    string? channel = null,
    string? eventName = null)
    => new(
      LogEvent.CurrentSchemaVersion,
      eventId,
      timestamp,
      severity,
      component,
      channel ?? LogChannelRegistry.Diagnostics,
      eventName ?? EventNameRegistry.HeartbeatTick,
      "message",
      Exception: null,
      OperationId: null,
      TraceId: null,
      SpanId: null,
      UserActionId: null,
      ProcessId: 100,
      ThreadId: null,
      LogEvent.EmptyProperties);

  [Fact]
  public async Task AggregateAsync_buckets_severity_component_channel_and_event_counts()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:1", baseTime, LogSeverity.Error, LogComponentRegistry.Engine, LogChannelRegistry.Diagnostics, EventNameRegistry.IterationError),
      CreateEvent("e:2", baseTime.AddMinutes(5), LogSeverity.Warn, LogComponentRegistry.Tray, LogChannelRegistry.UI, EventNameRegistry.HeartbeatTick),
      CreateEvent("e:3", baseTime.AddMinutes(10), LogSeverity.Error, LogComponentRegistry.Engine, LogChannelRegistry.Diagnostics, EventNameRegistry.IterationError),
    };

    var service = new LogAggregationService();
    var request = new LogAggregationRequest(
      LogQueryMatchAllExpression.Instance,
      TimeSpan.FromMinutes(10),
      TimeZoneInfo.Utc);

    var result = await service.AggregateAsync(
      new FakeEventSource(events),
      request,
      generation: 1,
      CancellationToken.None);

    result.IsComplete.Should().BeTrue();
    result.TotalMatchedEvents.Should().Be(3);
    result.SeverityBuckets.Should().HaveCount(2);
    result.SeverityBuckets[0].ErrorCount.Should().Be(1);
    result.SeverityBuckets[1].ErrorCount.Should().Be(1);
    result.ComponentCounts.Should().Contain(item => item.Name == LogComponentRegistry.Engine && item.Count == 2);
    result.ComponentCounts.Should().Contain(item => item.Name == LogComponentRegistry.Tray && item.Count == 1);
    result.ChannelCounts.Should().Contain(item => item.Name == LogChannelRegistry.Diagnostics && item.Count == 2);
    result.EventNameCounts.Should().Contain(item => item.Name == EventNameRegistry.IterationError && item.Count == 2);
  }

  [Fact]
  public async Task AggregateAsync_zero_fills_empty_time_buckets_in_local_timezone()
  {
    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
      OperatingSystem.IsWindows() ? "China Standard Time" : "Asia/Shanghai");
    var localStart = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Unspecified);
    var first = new DateTimeOffset(localStart, timeZone.GetUtcOffset(localStart));
    var secondLocal = localStart.AddMinutes(20);
    var second = new DateTimeOffset(secondLocal, timeZone.GetUtcOffset(secondLocal));
    var events = new[]
    {
      CreateEvent("e:1", first, LogSeverity.Error),
      CreateEvent("e:2", second, LogSeverity.Error),
    };

    var service = new LogAggregationService();
    var request = new LogAggregationRequest(
      LogQueryMatchAllExpression.Instance,
      TimeSpan.FromMinutes(10),
      timeZone);

    var result = await service.AggregateAsync(
      new FakeEventSource(events),
      request,
      generation: 1,
      CancellationToken.None);

    result.SeverityBuckets.Should().HaveCount(3);
    result.SeverityBuckets[0].ErrorCount.Should().Be(1);
    result.SeverityBuckets[1].ErrorCount.Should().Be(0);
    result.SeverityBuckets[2].ErrorCount.Should().Be(1);
  }

  [Fact]
  public async Task AggregateAsync_respects_dst_boundaries_when_bucketing()
  {
    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
      OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
    var beforeGap = new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero);
    var afterGap = new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:before", beforeGap, LogSeverity.Error),
      CreateEvent("e:after", afterGap, LogSeverity.Error),
    };

    var service = new LogAggregationService();
    var request = new LogAggregationRequest(
      LogQueryMatchAllExpression.Instance,
      TimeSpan.FromHours(1),
      timeZone);

    var result = await service.AggregateAsync(
      new FakeEventSource(events),
      request,
      generation: 1,
      CancellationToken.None);

    result.SeverityBuckets.Should().NotBeEmpty();
    result.SeverityBuckets.Sum(bucket => bucket.ErrorCount).Should().Be(2);
    result.SeverityBuckets.Should().OnlyContain(bucket => bucket.ErrorCount <= 1);
  }

  [Fact]
  public async Task AggregateAsync_honors_query_expression_filter()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:error", baseTime, LogSeverity.Error, LogComponentRegistry.Engine),
      CreateEvent("e:info", baseTime.AddMinutes(1), LogSeverity.Info, LogComponentRegistry.Tray),
    };

    var service = new LogAggregationService();
    var request = new LogAggregationRequest(
      LogQueryParser.Parse("severity:ERROR").Expression!,
      TimeSpan.FromMinutes(10),
      TimeZoneInfo.Utc);

    var result = await service.AggregateAsync(
      new FakeEventSource(events),
      request,
      generation: 1,
      CancellationToken.None);

    result.TotalMatchedEvents.Should().Be(1);
    result.ComponentCounts.Should().ContainSingle(item => item.Name == LogComponentRegistry.Engine && item.Count == 1);
  }

  [Fact]
  public async Task AggregateAsync_returns_cancelled_result_without_full_scan()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
    var events = Enumerable.Range(0, 200)
      .Select(index => CreateEvent($"e:{index}", baseTime.AddSeconds(index)))
      .ToArray();

    using var cancellation = new CancellationTokenSource();
    var service = new LogAggregationService();
    var request = new LogAggregationRequest(
      LogQueryMatchAllExpression.Instance,
      TimeSpan.FromMinutes(1),
      TimeZoneInfo.Utc);

    cancellation.Cancel();
    var result = await service.AggregateAsync(
      new FakeEventSource(events),
      request,
      generation: 1,
      cancellation.Token);

    result.IsCancelled.Should().BeTrue();
    result.IsComplete.Should().BeFalse();
  }

  [Fact]
  public async Task AggregateAsync_rejects_stale_generation_results()
  {
    var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
    var events = new[]
    {
      CreateEvent("e:1", baseTime, LogSeverity.Error),
      CreateEvent("e:2", baseTime.AddMinutes(1), LogSeverity.Error),
    };

    var service = new LogAggregationService();
    service.NotifyGenerationChanged();
    service.NotifyGenerationChanged();

    var request = new LogAggregationRequest(
      LogQueryMatchAllExpression.Instance,
      TimeSpan.FromMinutes(10),
      TimeZoneInfo.Utc);

    var result = await service.AggregateAsync(
      new FakeEventSource(events),
      request,
      generation: 1,
      CancellationToken.None);

    result.IsSuperseded.Should().BeTrue();
    result.IsComplete.Should().BeFalse();
  }

  private sealed class FakeEventSource : ILogEventSource
  {
    private readonly IReadOnlyList<LogEvent> _events;

    public FakeEventSource(IReadOnlyList<LogEvent> events) => _events = events;

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
      foreach (var logEvent in _events)
      {
        cancellationToken.ThrowIfCancellationRequested();
        yield return logEvent;
      }

      await Task.CompletedTask;
    }
  }
}
