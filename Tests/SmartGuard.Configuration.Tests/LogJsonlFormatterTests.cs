using System.Text.Json;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogJsonlFormatterTests
{
  [Fact]
  public void FormatLine_produces_valid_json()
  {
    var entry = new LogJsonlEntry(
      new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
      "INFO", "Engine", "AppStarted", "Application started", null, null, null, null, null, null);

    var json = LogJsonlFormatter.FormatLine(entry);

    var parsed = JsonDocument.Parse(json);
    parsed.RootElement.GetProperty("level").GetString().Should().Be("INFO");
    parsed.RootElement.GetProperty("category").GetString().Should().Be("Engine");
    parsed.RootElement.GetProperty("eventName").GetString().Should().Be("AppStarted");
    parsed.RootElement.GetProperty("message").GetString().Should().Be("Application started");
  }

  [Fact]
  public void FormatLine_includes_optional_fields_when_present()
  {
    var entry = new LogJsonlEntry(
      new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
      "WARN", "Engine", "RecoveryDegraded", "Degraded",
      "op-abc", "trace-123", "span-456", "ua-789", 1234, 5678);

    var json = LogJsonlFormatter.FormatLine(entry);

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
    var entry = new LogJsonlEntry(
      new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
      "INFO", "Engine", null, "Simple message", null, null, null, null, null, null);

    var json = LogJsonlFormatter.FormatLine(entry);

    var parsed = JsonDocument.Parse(json);
    parsed.RootElement.TryGetProperty("eventName", out _).Should().BeFalse();
    parsed.RootElement.TryGetProperty("operationId", out _).Should().BeFalse();
  }

  [Fact]
  public void TryParseLine_parses_human_readable_line()
  {
    var line = "[WARN] 2026-08-01 10:01:00 [Engine] RecoveryDegraded op=op-abc";

    var result = LogJsonlFormatter.TryParseLine(line, out var entry);

    result.Should().BeTrue();
    entry!.Level.Should().Be("WARN");
    entry.Category.Should().Be("Engine");
    entry.OperationId.Should().Be("op-abc");
  }

  [Fact]
  public void TryParseLine_returns_false_for_malformed_line()
  {
    var result = LogJsonlFormatter.TryParseLine("garbage line", out var entry);
    result.Should().BeFalse();
    entry.Should().BeNull();
  }

  [Fact]
  public void TryParseLine_parses_trace_and_span_from_context_suffix()
  {
    var line = "[ERROR] 2026-08-01 10:02:00 [GuardianLoop] IterationError op=op-abc trace=trace-xyz span=span-456";

    var result = LogJsonlFormatter.TryParseLine(line, out var entry);

    result.Should().BeTrue();
    entry!.OperationId.Should().Be("op-abc");
    entry.TraceId.Should().Be("trace-xyz");
    entry.SpanId.Should().Be("span-456");
  }
}
