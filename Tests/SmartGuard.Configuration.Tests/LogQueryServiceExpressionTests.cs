using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogQueryServiceExpressionTests
{
  private sealed class CollectingProgress : IProgress<LogQueryProgress>
  {
    public List<LogQueryProgress> Reports { get; } = [];

    public void Report(LogQueryProgress value) => Reports.Add(value);
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
    }
  }

  private static LogEvent CreateEvent(
    string severity,
    string component,
    string message,
    string? operationId = null)
  {
    return LogEventFactory.Create(
      Enum.Parse<LogSeverity>(severity, ignoreCase: true),
      component,
      LogChannelRegistry.Diagnostics,
      EventNameRegistry.AppStarted,
      message,
      operationId: operationId);
  }

  [Fact]
  public async Task QueryAsync_evaluates_AND_expression()
  {
    var events = new[]
    {
      CreateEvent("Error", "Engine", "timeout while starting"),
      CreateEvent("Error", "Tray", "timeout while starting"),
      CreateEvent("Info", "Engine", "healthy"),
    };
    var expression = LogQueryParser.Parse("severity:ERROR AND component:Engine").Expression!;
    var service = new LogQueryService();

    var result = await service.QueryAsync(
      new FakeEventSource(events),
      expression,
      cursor: null,
      pageSize: 10,
      progress: null,
      CancellationToken.None);

    result.Matches.Should().HaveCount(1);
    result.Matches[0].Category.Should().Be("Engine");
    result.IsScanComplete.Should().BeTrue();
    result.ExactTotalCount.Should().Be(1);
  }

  [Fact]
  public async Task QueryAsync_reports_progressive_count_before_completion()
  {
    var events = Enumerable.Range(0, 5)
      .Select(i => CreateEvent("Info", "Engine", $"message-{i}"))
      .ToArray();
    var expression = LogQueryParser.Parse("component:Engine").Expression!;
    var progress = new CollectingProgress();
    var service = new LogQueryService();

    await service.QueryAsync(
      new FakeEventSource(events),
      expression,
      cursor: null,
      pageSize: 10,
      progress,
      CancellationToken.None);

    progress.Reports.Should().NotBeEmpty();
    progress.Reports.Last().IsComplete.Should().BeTrue();
    progress.Reports.Last().ExactTotal.Should().Be(5);
    progress.Reports.Any(p => !p.IsComplete && p.MatchCount > 0).Should().BeTrue();
  }

  [Fact]
  public async Task QueryAsync_honours_cancellation_without_late_page()
  {
    var events = Enumerable.Range(0, 100)
      .Select(i => CreateEvent("Info", "Engine", $"message-{i}"))
      .ToArray();
    var expression = LogQueryParser.Parse("component:Engine").Expression!;
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var service = new LogQueryService();

    var result = await service.QueryAsync(
      new FakeEventSource(events),
      expression,
      cursor: null,
      pageSize: 10,
      progress: null,
      cts.Token);

    result.IsScanComplete.Should().BeFalse();
    result.Matches.Should().BeEmpty();
  }

  [Fact]
  public void Invalid_expression_does_not_produce_query_result()
  {
    var result = LogQueryParser.Parse("bogus:value");

    result.IsValid.Should().BeFalse();
    result.Expression.Should().BeNull();
  }
}
