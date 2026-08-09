using System.Text.Json;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

public sealed class LogTableRowTests
{
  [Fact]
  public void Structured_event_projects_level_time_source_event_and_status()
  {
    var source = CreateEvent(
      severity: LogSeverity.Info,
      component: "PowerManager",
      eventName: "PlanChanged",
      message: "平衡模式已应用",
      timestamp: new DateTimeOffset(2026, 8, 8, 12, 34, 56, TimeSpan.Zero));

    var row = LogTableRow.FromEvent(source);

    row.SourceEvent.Should().BeSameAs(source);
    row.LevelText.Should().Be("信息");
    row.TimeText.Should().Be(source.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
    row.SourceText.Should().Be("PowerManager");
    row.EventText.Should().Be("PlanChanged：平衡模式已应用");
    row.StatusText.Should().Be("—");
  }

  [Fact]
  public void Legacy_event_uses_compatibility_source_without_losing_message()
  {
    var source = CreateEvent(
      severity: LogSeverity.Warn,
      component: LogComponentRegistry.Legacy,
      channel: LogChannelRegistry.Compatibility,
      eventName: null,
      message: "旧格式日志内容");

    var row = LogTableRow.FromEvent(source);

    row.SourceText.Should().Be(LogComponentRegistry.Legacy);
    row.EventText.Should().Be("旧格式日志内容");
    row.SourceEvent.Message.Should().Be("旧格式日志内容");
    row.StatusText.Should().Be("—");
  }

  [Fact]
  public void Unregistered_status_property_projects_dash_when_coverage_has_no_confirmed_keys()
  {
    var source = CreateEvent(
      severity: LogSeverity.Info,
      message: "success completed",
      properties: new Dictionary<string, JsonElement>
      {
        ["status"] = JsonDocument.Parse("\"success\"").RootElement,
      });

    LogTableRow.FromEvent(source).StatusText.Should().Be("—");
  }

  [Fact]
  public void Registered_outcome_property_projects_localized_status_text()
  {
    var source = CreateEvent(
      properties: new Dictionary<string, JsonElement>
      {
        ["outcome"] = JsonDocument.Parse("\"handled\"").RootElement,
      });

    LogTableRow.FromEvent(source).StatusText.Should().Be("已处理");
  }

  [Theory]
  [InlineData("\"unexpected\"")]
  [InlineData("42")]
  public void Unknown_or_non_string_outcome_projects_dash(string json)
  {
    var source = CreateEvent(
      properties: new Dictionary<string, JsonElement>
      {
        [LogEventOutcome.PropertyName] = JsonDocument.Parse(json).RootElement,
      });

    LogTableRow.FromEvent(source).StatusText.Should().Be("—");
  }

  [Fact]
  public void Severity_and_message_never_infer_status()
  {
    var source = CreateEvent(
      severity: LogSeverity.Error,
      message: "failed: success was not reached");

    var row = LogTableRow.FromEvent(source);

    row.LevelText.Should().Be("错误");
    row.EventText.Should().Contain("failed");
    row.StatusText.Should().Be("—");
  }

  [Fact]
  public void Level_event_and_status_cells_expose_no_decorative_icon()
  {
    typeof(LogTableRow)
      .GetProperties()
      .Should()
      .NotContain(property => property.Name.Contains("Icon", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void Exception_root_preserves_source_identity_and_export_text()
  {
    var source = CreateEvent(
      severity: LogSeverity.Error,
      message: "exception root",
      exception: new LogExceptionInfo(
        "System.InvalidOperationException",
        "bad state",
        null,
        null));

    var row = LogTableRow.FromEvent(source);

    row.SourceEvent.Should().BeSameAs(source);
    row.IsExceptionRoot.Should().BeTrue();
    row.ExceptionGroupRootEventId.Should().Be(source.EventId);
    row.ExportText.Should().Contain("System.InvalidOperationException");
    row.ExportText.Should().Contain("bad state");
  }

  [Fact]
  public void Structured_exception_export_includes_nested_exception_and_stack_lines()
  {
    var source = CreateEvent(
      severity: LogSeverity.Error,
      message: "write failed",
      exception: new LogExceptionInfo(
        "System.InvalidOperationException",
        "write failed",
        " at Outer()",
        new LogExceptionInfo(
          "System.IO.IOException",
          "disk full",
          " at Inner()",
          null)));

    var exportText = LogTableRow.FromEvent(source).ExportText;

    exportText.Should().Contain("System.InvalidOperationException");
    exportText.Should().Contain(" at Outer()");
    exportText.Should().Contain("System.IO.IOException");
    exportText.Should().Contain(" at Inner()");
  }

  [Fact]
  public void Correlation_keys_remain_available_from_projected_row()
  {
    var source = CreateEvent(operationId: "op-42");

    var row = LogTableRow.FromEvent(source);

    row.CorrelationKey.Should().BeEquivalentTo(
      new LogOperationCorrelationKey(LogCorrelationKeyKind.OperationId, "op-42"));
  }

  private static LogEvent CreateEvent(
    LogSeverity severity = LogSeverity.Info,
    string component = "Engine",
    string channel = "Runtime",
    string? eventName = "TestEvent",
    string message = "test event",
    DateTimeOffset? timestamp = null,
    LogExceptionInfo? exception = null,
    string? operationId = null,
    IReadOnlyDictionary<string, JsonElement>? properties = null)
    => new(
      LogEvent.CurrentSchemaVersion,
      Guid.NewGuid().ToString("N"),
      timestamp ?? new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero),
      severity,
      component,
      channel,
      eventName,
      message,
      exception,
      operationId,
      TraceId: null,
      SpanId: null,
      UserActionId: null,
      ProcessId: null,
      ThreadId: null,
      properties ?? LogEvent.EmptyProperties);
}
