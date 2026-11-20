using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogQueryRegexTests
{
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

  private static LogEvent CreateEvent(string message)
    => LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Diagnostics,
      EventNameRegistry.AppStarted,
      message);

  [Fact]
  public void Parse_tilde_operator_uses_regex_semantics()
  {
    var result = LogQueryParser.Parse("message~^engine-\\d+$");

    result.IsValid.Should().BeTrue();
    var field = result.Expression.Should().BeOfType<LogQueryFieldExpression>().Subject;
    field.Operator.Should().Be(LogQueryOperator.Regex);
  }

  [Fact]
  public void Validate_rejects_expression_longer_than_4096_characters()
  {
    var text = "message:" + new string('x', LogQueryLimits.MaxExpressionLength);
    var parsed = LogQueryParser.Parse(text);

    parsed.IsValid.Should().BeTrue();
    var validation = LogQueryExpressionValidator.Validate(parsed.Expression!, text);

    validation.IsValid.Should().BeFalse();
    validation.ErrorMessage.Should().Contain("4096");
  }

  [Fact]
  public void Validate_rejects_ast_depth_over_32()
  {
    var text = string.Join(" AND ", Enumerable.Range(0, 33).Select(i => $"message:term{i}"));
    var parsed = LogQueryParser.Parse(text);

    parsed.IsValid.Should().BeTrue();
    var validation = LogQueryExpressionValidator.Validate(parsed.Expression!, text);

    validation.IsValid.Should().BeFalse();
    validation.ErrorMessage.Should().Contain("32");
  }

  [Fact]
  public async Task QueryAsync_regex_matches_events()
  {
    var events = new[]
    {
      CreateEvent("engine-1 started"),
      CreateEvent("tray idle"),
      CreateEvent("engine-2 stopped"),
    };
    var expression = LogQueryParser.Parse("message~engine-\\d+").Expression!;
    var service = new LogQueryService();

    var result = await service.QueryAsync(
      new FakeEventSource(events),
      expression,
      cursor: null,
      pageSize: 10,
      progress: null,
      CancellationToken.None,
      ignoreCase: true);

    result.TotalCount.Should().Be(2);
    result.OrderedMatches.Should().HaveCount(2);
    result.IsScanComplete.Should().BeTrue();
  }

  [Fact]
  public async Task QueryAsync_first_regex_timeout_aborts_entire_scan()
  {
    var events = Enumerable.Range(0, 50)
      .Select(i => CreateEvent(new string('a', 30)))
      .ToArray();
    var expression = LogQueryParser.Parse("message~(a+)+b").Expression!;
    var service = new LogQueryService();

    var result = await service.QueryAsync(
      new FakeEventSource(events),
      expression,
      cursor: null,
      pageSize: 100,
      progress: null,
      CancellationToken.None,
      ignoreCase: true);

    result.AbortedDueToRegexTimeout.Should().BeTrue();
    result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    result.IsScanComplete.Should().BeFalse();
    result.ScannedCount.Should().BeLessThan(50);
  }

  [Fact]
  public async Task QueryAsync_collects_ordered_match_ids()
  {
    var events = new[]
    {
      CreateEvent("alpha"),
      CreateEvent("beta"),
      CreateEvent("alpha again"),
    };
    var expression = LogQueryParser.Parse("message~alpha").Expression!;
    var service = new LogQueryService();

    var result = await service.QueryAsync(
      new FakeEventSource(events),
      expression,
      cursor: null,
      pageSize: 10,
      progress: null,
      CancellationToken.None,
      ignoreCase: true);

    result.OrderedMatches.Select(m => m.Ordinal).Should().Equal(1, 2);
    result.OrderedMatches.Select(m => m.EventId).Should().OnlyHaveUniqueItems();
  }
}
