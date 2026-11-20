using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogFilterBuilderTests
{
  [Fact]
  public void Create_returns_builder_with_defaults()
  {
    var spec = LogFilterBuilder.Create().Build();

    spec.Page.Should().Be(1);
    spec.PageSize.Should().Be(50);
  }

  [Fact]
  public void WithKeyword_sets_keyword()
  {
    var spec = LogFilterBuilder.Create().WithKeyword("crash").Build();
    spec.Keyword.Should().Be("crash");
  }

  [Fact]
  public void WithLevel_sets_level_uppercase()
  {
    var spec = LogFilterBuilder.Create().WithLevel("error").Build();
    spec.Level.Should().Be("ERROR");
  }

  [Fact]
  public void WithOperationId_sets_operation_id()
  {
    var spec = LogFilterBuilder.Create().WithOperationId("op-abc").Build();
    spec.OperationId.Should().Be("op-abc");
  }

  [Fact]
  public void WithTimeRange_sets_start_and_end()
  {
    var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    var end = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);

    var spec = LogFilterBuilder.Create().WithTimeRange(start, end).Build();

    spec.StartTime.Should().Be(start);
    spec.EndTime.Should().Be(end);
  }

  [Fact]
  public void WithPagination_sets_page_and_page_size()
  {
    var spec = LogFilterBuilder.Create().WithPagination(3, 25).Build();
    spec.Page.Should().Be(3);
    spec.PageSize.Should().Be(25);
  }

  [Fact]
  public void Chained_calls_accumulate_all_fields()
  {
    var spec = LogFilterBuilder.Create()
      .WithKeyword("timeout")
      .WithLevel("WARN")
      .WithCategory("Engine")
      .WithOperationId("op-xyz")
      .WithEventName("RecoveryDegraded")
      .WithProcessId(1234)
      .WithThreadId(5678)
      .WithPagination(2, 100)
      .Build();

    spec.Keyword.Should().Be("timeout");
    spec.Level.Should().Be("WARN");
    spec.Category.Should().Be("Engine");
    spec.OperationId.Should().Be("op-xyz");
    spec.EventName.Should().Be("RecoveryDegraded");
    spec.ProcessId.Should().Be(1234);
    spec.ThreadId.Should().Be(5678);
    spec.Page.Should().Be(2);
    spec.PageSize.Should().Be(100);
  }

  [Fact]
  public void BuildExpression_matches_equivalent_text_query()
  {
    var spec = LogFilterBuilder.Create()
      .WithLevel("ERROR")
      .WithCategory("Engine")
      .WithKeyword("timeout")
      .Build();

    var fromChips = spec.ToExpression();
    var fromText = LogQueryParser.Parse("level:ERROR component:Engine timeout").Expression;

    fromText.Should().NotBeNull();

    var events = new[]
    {
      LogEventFactory.Create(LogSeverity.Error, LogComponentRegistry.Engine, LogChannelRegistry.Diagnostics, EventNameRegistry.AppStarted, "timeout while starting"),
      LogEventFactory.Create(LogSeverity.Error, LogComponentRegistry.Tray, LogChannelRegistry.Diagnostics, EventNameRegistry.AppStarted, "timeout while starting"),
      LogEventFactory.Create(LogSeverity.Info, LogComponentRegistry.Engine, LogChannelRegistry.Diagnostics, EventNameRegistry.AppStarted, "healthy"),
    };

    foreach (var logEvent in events)
    {
      LogQueryExpressionEvaluator.Matches(logEvent, fromChips)
        .Should().Be(LogQueryExpressionEvaluator.Matches(logEvent, fromText!));
    }
  }
}
