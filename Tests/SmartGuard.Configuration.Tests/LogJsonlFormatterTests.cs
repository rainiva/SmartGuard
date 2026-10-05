using System.Text.Json;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogJsonlFormatterTests
{
  [Fact]
  public void FormatLine_keeps_the_existing_depth_failure_contract()
  {
    using var document = JsonDocument.Parse(new string('[', 63) + "0" + new string(']', 63));
    var item = LogEventFactory.Create(LogSeverity.Warn, "Engine", "Power", "IterationError", "depth")
      with { Properties = new Dictionary<string, JsonElement> { ["value"] = document.RootElement.Clone() } };

    Action format = () => LogJsonlFormatter.FormatLine(item);
    format.Should().Throw<JsonException>();
    LogJsonlFormatter.TryParseLine(LogJsonlFormatter.FormatLine(item with { Properties = LogEvent.EmptyProperties }), out _)
      .Should().BeTrue("a failed event must not leave shared writer state behind");
  }

  [Fact]
  public void Concurrent_formatting_does_not_mix_event_contents()
  {
    Parallel.For(0, 128, i =>
    {
      var item = LogEventFactory.Create(LogSeverity.Warn, "Engine", "Power", "IterationError", "message-" + i);
      LogJsonlFormatter.TryParseLine(LogJsonlFormatter.FormatLine(item), out var parsed).Should().BeTrue();
      parsed!.EventId.Should().Be(item.EventId);
      parsed.Message.Should().Be(item.Message);
    });
  }

  [Fact]
  public void FormatLine_preserves_fixed_schema_escaping_optional_fields_and_nested_properties()
  {
    using var property = JsonDocument.Parse("{\"中文\":[null,true,12.5,{\"x\":\"<>&\\n\"}]}");
    var logEvent = new LogEvent(1, "event-1", DateTimeOffset.Parse("2026-10-05T08:00:00+08:00"),
      LogSeverity.Warn, "Engine", "Power", "IterationError", "中文 <>& \"\\\n",
      new LogExceptionInfo("Outer", "外层", null, new LogExceptionInfo("Inner", "内层", "line\n2", null)),
      "op", "trace", "span", "action", 12, 34,
      new Dictionary<string, JsonElement> { ["detail"] = property.RootElement.Clone() });

    var line = LogJsonlFormatter.FormatLine(logEvent);

    line.Should().Be("{\"schemaVersion\":1,\"eventId\":\"event-1\",\"timestamp\":\"2026-10-05T00:00:00.0000000+00:00\",\"severity\":\"Warn\",\"component\":\"Engine\",\"channel\":\"Power\",\"message\":\"中文 <>& \\\"\\\\\\n\",\"eventName\":\"IterationError\",\"exception\":{\"type\":\"Outer\",\"message\":\"外层\",\"inner\":{\"type\":\"Inner\",\"message\":\"内层\",\"stackTrace\":\"line\\n2\"}},\"operationId\":\"op\",\"traceId\":\"trace\",\"spanId\":\"span\",\"userActionId\":\"action\",\"processId\":12,\"threadId\":34,\"properties\":{\"detail\":{\"中文\":[null,true,12.5,{\"x\":\"<>&\\n\"}]}}}");
    LogJsonlFormatter.TryParseLine(line, out var parsed).Should().BeTrue();
    parsed!.Exception!.Inner!.StackTrace.Should().Be("line\n2");
  }

  [Fact]
  public void FormatLine_produces_valid_json()
  {
    var logEvent = LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      EventNameRegistry.AppStarted,
      "Application started");

    var json = LogJsonlFormatter.FormatLine(logEvent);

    var parsed = JsonDocument.Parse(json);
    parsed.RootElement.GetProperty("severity").GetString().Should().Be("Info");
    parsed.RootElement.GetProperty("component").GetString().Should().Be("Engine");
    parsed.RootElement.GetProperty("eventName").GetString().Should().Be("AppStarted");
    parsed.RootElement.GetProperty("message").GetString().Should().Be("Application started");
  }

  [Fact]
  public void FormatLine_includes_optional_fields_when_present()
  {
    var logEvent = LogEventFactory.Create(
      LogSeverity.Warn,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Recovery,
      EventNameRegistry.RecoveryDegraded,
      "Degraded",
      operationId: "op-abc",
      traceId: "trace-123",
      spanId: "span-456",
      userActionId: "ua-789",
      processId: 1234,
      threadId: 5678);

    var json = LogJsonlFormatter.FormatLine(logEvent);

    var parsed = JsonDocument.Parse(json);
    parsed.RootElement.GetProperty("operationId").GetString().Should().Be("op-abc");
    parsed.RootElement.GetProperty("traceId").GetString().Should().Be("trace-123");
    parsed.RootElement.GetProperty("spanId").GetString().Should().Be("span-456");
    parsed.RootElement.GetProperty("userActionId").GetString().Should().Be("ua-789");
    parsed.RootElement.GetProperty("processId").GetInt32().Should().Be(1234);
    parsed.RootElement.GetProperty("threadId").GetInt32().Should().Be(5678);
  }

  [Fact]
  public void FormatLine_omits_null_fields()
  {
    var logEvent = LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      EventNameRegistry.AppStarted,
      "Simple message");

    var json = LogJsonlFormatter.FormatLine(logEvent);

    var parsed = JsonDocument.Parse(json);
    parsed.RootElement.TryGetProperty("processId", out _).Should().BeFalse();
    parsed.RootElement.TryGetProperty("threadId", out _).Should().BeFalse();
  }

  [Fact]
  public void TryParseLine_parses_structured_json_line()
  {
    var original = LogEventFactory.Create(
      LogSeverity.Warn,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Recovery,
      EventNameRegistry.RecoveryDegraded,
      "Degraded",
      operationId: "op-abc");

    var result = LogJsonlFormatter.TryParseLine(LogJsonlFormatter.FormatLine(original), out var logEvent);

    result.Should().BeTrue();
    logEvent!.Component.Should().Be("Engine");
    logEvent.Severity.Should().Be(LogSeverity.Warn);
    logEvent.OperationId.Should().Be("op-abc");
  }

  [Fact]
  public void TryParseLine_returns_false_for_malformed_line()
  {
    var result = LogJsonlFormatter.TryParseLine("garbage line", out var logEvent);
    result.Should().BeFalse();
    logEvent.Should().BeNull();
  }

  [Fact]
  public void TryParseLine_returns_false_for_legacy_human_readable_line()
  {
    var line = "[ERROR] 2026-08-01 10:02:00 [GuardianLoop] IterationError op=op-abc trace=trace-xyz span=span-456";

    var result = LogJsonlFormatter.TryParseLine(line, out var logEvent);

    result.Should().BeFalse();
    logEvent.Should().BeNull();
  }
}
