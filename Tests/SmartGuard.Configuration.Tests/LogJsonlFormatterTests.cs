using System.Text.Json;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogJsonlFormatterTests
{
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
