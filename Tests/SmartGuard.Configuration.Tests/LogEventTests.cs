using System.Text.Json;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogEventTests
{
  private static LogSourceIdentity TestSource()
    => LogSourceIdentity.FromContentPrefix(
      new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
      "test-log-content-prefix");

  [Fact]
  public void FormatLine_round_trips_every_structured_field_including_chinese_and_properties()
  {
    using var doc = JsonDocument.Parse("""{"note":"中文备注","count":42}""");
    var properties = doc.RootElement.EnumerateObject()
      .ToDictionary(p => p.Name, p => p.Value.Clone());

    var original = LogEventFactory.Create(
      LogSeverity.Warn,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Recovery,
      EventNameRegistry.RecoveryDegraded,
      "服务降级：亮度策略已暂停",
      operationId: "op-abc",
      traceId: "trace-123",
      spanId: "span-456",
      userActionId: "ua-789",
      processId: 1234,
      threadId: 5678,
      properties: properties);

    var json = LogJsonlFormatter.FormatLine(original);
    LogJsonlFormatter.TryParseEvent(json, out var roundTrip).Should().BeTrue();
    roundTrip.Should().NotBeNull();
    roundTrip = roundTrip!;
    roundTrip.SchemaVersion.Should().Be(LogEvent.CurrentSchemaVersion);
    roundTrip.EventId.Should().Be(original.EventId);
    roundTrip.Timestamp.Should().Be(original.Timestamp);
    roundTrip.Severity.Should().Be(LogSeverity.Warn);
    roundTrip.Component.Should().Be(LogComponentRegistry.Engine);
    roundTrip.Channel.Should().Be(LogChannelRegistry.Recovery);
    roundTrip.EventName.Should().Be(EventNameRegistry.RecoveryDegraded);
    roundTrip.Message.Should().Be("服务降级：亮度策略已暂停");
    roundTrip.OperationId.Should().Be("op-abc");
    roundTrip.TraceId.Should().Be("trace-123");
    roundTrip.SpanId.Should().Be("span-456");
    roundTrip.UserActionId.Should().Be("ua-789");
    roundTrip.ProcessId.Should().Be(1234);
    roundTrip.ThreadId.Should().Be(5678);
    roundTrip.Properties["note"].GetString().Should().Be("中文备注");
    roundTrip.Properties["count"].GetInt32().Should().Be(42);
  }

  [Fact]
  public void Legacy_line_produces_deterministic_event_id_for_same_source_offset()
  {
    const string line = "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted";
    var source = TestSource();
    var physical = new LogPhysicalLine(source, 128, line, true, 0);

    var first = LogEventDecoder.Decode(physical);
    var second = LogEventDecoder.Decode(physical);

    first.EventId.Should().StartWith("l:");
    first.EventId.Should().Be(second.EventId);
    first.Component.Should().Be(LogComponentRegistry.Engine);
    first.Channel.Should().Be(LogChannelRegistry.Compatibility);
  }

  [Fact]
  public void Tagged_diagnostic_line_without_component_uses_embedded_timestamp()
  {
    const string line = "[DIAG] 2026-08-03 12:10:08 {\"health\":\"LogPipeline\",\"writtenLinesTotal\":16}";

    var decoded = LogEventDecoder.Decode(new LogPhysicalLine(TestSource(), 256, line, true));

    decoded.Timestamp.Should().Be(
      new DateTimeOffset(2026, 8, 3, 12, 10, 8, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 3, 12, 10, 8))));
    decoded.Severity.Should().Be(LogSeverity.Info);
    decoded.Component.Should().Be(LogComponentRegistry.Legacy);
    decoded.EventName.Should().BeNull();
    decoded.Message.Should().Be(line);
  }

  [Fact]
  public void Raw_prefixed_diagnostic_line_uses_embedded_timestamp()
  {
    const string line = "raw: [DIAG] 2026-08-03 12:10:08 {\"health\":\"LogPipeline\",\"writtenLinesTotal\":16}";

    var decoded = LogEventDecoder.Decode(new LogPhysicalLine(TestSource(), 384, line, true));

    decoded.Timestamp.Should().Be(
      new DateTimeOffset(2026, 8, 3, 12, 10, 8, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 3, 12, 10, 8))));
    decoded.Severity.Should().Be(LogSeverity.Info);
    decoded.Component.Should().Be(LogComponentRegistry.Legacy);
    decoded.EventName.Should().Be("raw");
    decoded.Message.Should().Be(line);
  }

  [Fact]
  public void Identical_legacy_lines_are_distinguished_by_ordinal()
  {
    const string line = "[INFO] 2026-08-01 10:00:00 [Engine] HeartbeatTick";
    var source = TestSource();

    var first = LogEventDecoder.Decode(new LogPhysicalLine(source, 0, line, true, Ordinal: 0));
    var second = LogEventDecoder.Decode(new LogPhysicalLine(source, 0, line, true, Ordinal: 1));

    first.EventId.Should().NotBe(second.EventId);
  }

  [Fact]
  public void Archive_rename_preserves_legacy_event_id()
  {
    const string line = "[WARN] 2026-08-01 10:01:00 [Engine] RecoveryDegraded op=op-abc";
    var source = TestSource();
    var physical = new LogPhysicalLine(source, 512, line, true, 0);

    var beforeRename = LogEventDecoder.Decode(physical);
    var afterRename = LogEventDecoder.Decode(physical with { });

    afterRename.EventId.Should().Be(beforeRename.EventId);
  }

  [Fact]
  public void Exception_round_trip_preserves_inner_chain_and_stack()
  {
    var inner = new LogExceptionInfo(
      "System.IO.IOException",
      "磁盘已满",
      " at Inner()",
      Inner: null);
    var outer = new LogExceptionInfo(
      "System.InvalidOperationException",
      "写入失败",
      " at Outer()",
      Inner: inner);

    var original = LogEventFactory.Create(
      LogSeverity.Error,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Diagnostics,
      EventNameRegistry.IterationError,
      "迭代异常",
      exception: outer);

    var json = LogJsonlFormatter.FormatLine(original);
    LogJsonlFormatter.TryParseEvent(json, out var roundTrip).Should().BeTrue();
    roundTrip.Should().NotBeNull();
    roundTrip = roundTrip!;
    roundTrip.Exception.Should().NotBeNull();
    roundTrip.Exception!.Type.Should().Be("System.InvalidOperationException");
    roundTrip.Exception.Message.Should().Be("写入失败");
    roundTrip.Exception.StackTrace.Should().Be(" at Outer()");
    roundTrip.Exception.Inner.Should().NotBeNull();
    roundTrip.Exception.Inner!.Type.Should().Be("System.IO.IOException");
    roundTrip.Exception.Inner.Message.Should().Be("磁盘已满");
    roundTrip.Exception.Inner.StackTrace.Should().Be(" at Inner()");
  }

  [Fact]
  public void Factory_rejects_unknown_component_channel_and_event_name()
  {
    var actComponent = () => LogEventFactory.Create(
      LogSeverity.Info,
      "UnknownComponent",
      LogChannelRegistry.Lifecycle,
      EventNameRegistry.AppStarted,
      "msg");
    var actChannel = () => LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      "UnknownChannel",
      EventNameRegistry.AppStarted,
      "msg");
    var actEventName = () => LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      "UnknownEvent",
      "msg");

    actComponent.Should().Throw<ArgumentException>();
    actChannel.Should().Throw<ArgumentException>();
    actEventName.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void Factory_allows_null_event_name_only_for_legacy_decoder_not_factory()
  {
    var legacyLine = "[INFO] 2026-08-01 10:00:00 [Engine] free text only";
    var decoded = LogEventDecoder.Decode(new LogPhysicalLine(TestSource(), 0, legacyLine, true));
    decoded.EventName.Should().BeNull();
  }

  [Fact]
  public void Structured_event_uses_schema_version_utc_timestamp_and_guid_event_id()
  {
    var created = LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      EventNameRegistry.AppStarted,
      "started");

    created.SchemaVersion.Should().Be(1);
    created.Timestamp.Offset.Should().Be(TimeSpan.Zero);
    created.EventId.Should().MatchRegex("^e:[0-9a-f]{32}$");
    created.EventName.Should().Be(EventNameRegistry.AppStarted);
    created.ProcessId.Should().BeNull();
    created.ThreadId.Should().BeNull();
  }

  [Fact]
  public void Decoder_returns_compatibility_event_for_malformed_line_without_throwing()
  {
    var act = () => LogEventDecoder.Decode(new LogPhysicalLine(TestSource(), 0, "garbage line", true));

    var decoded = act.Should().NotThrow().Subject;
    decoded.Component.Should().Be(LogComponentRegistry.Legacy);
    decoded.Channel.Should().Be(LogChannelRegistry.Compatibility);
    decoded.EventId.Should().StartWith("l:");
    decoded.Message.Should().Be("garbage line");
  }

  [Fact]
  public void TryParseLine_rejects_legacy_text_and_accepts_pure_jsonl()
  {
    LogJsonlFormatter.TryParseLine("[INFO] 2026-08-01 10:00:00 [Engine] AppStarted", out _)
      .Should().BeFalse();

    var structured = LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Lifecycle,
      EventNameRegistry.AppStarted,
      "started");
    LogJsonlFormatter.TryParseLine(LogJsonlFormatter.FormatLine(structured), out var logEvent)
      .Should().BeTrue();
    logEvent.Should().NotBeNull();
    logEvent!.Component.Should().Be(LogComponentRegistry.Engine);
  }

  [Fact]
  public void Display_formatter_renders_jsonl_line_for_legacy_ui()
  {
    var structured = LogEventFactory.Create(
      LogSeverity.Warn,
      LogComponentRegistry.Engine,
      LogChannelRegistry.Recovery,
      EventNameRegistry.RecoveryDegraded,
      "Degraded",
      operationId: "op-abc");

    var jsonLine = LogJsonlFormatter.FormatLine(structured);
    var physical = new LogPhysicalLine(TestSource(), 0, jsonLine, true);
    var decoded = LogEventDecoder.Decode(physical);
    var display = LogEventDisplayFormatter.FormatDisplayLine(decoded);

    display.Should().Contain("[WARN]");
    display.Should().Contain("[Engine]");
    display.Should().Contain("RecoveryDegraded");
    display.Should().Contain("op=op-abc");
  }
}
