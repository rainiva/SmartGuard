using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class StructuredLogPipelineTests
{
  private static LogEvent SampleEngineEvent(string message, string eventName = EventNameRegistry.AppStarted)
    => LogEventFactory.Create(
      LogSeverity.Info,
      LogComponentRegistry.Engine,
      LogEventChannels.ForEventName(eventName),
      eventName,
      message,
      processId: 1234,
      threadId: 1);

  private static LogPipelineOptions TinyOptions() => new()
  {
    NormalQueueCapacity = 8,
    HighQueueCapacity = 8,
    MaxBatchSize = 200,
    FlushIntervalMs = 60_000,
  };

  [Fact]
  public void FlushOnce_delivers_typed_events_in_enqueue_order()
  {
    var written = new List<LogEvent>();
    var pipeline = new LogPipeline(batch => { written.AddRange(batch); return LogBatchWriteResult.Primary(batch.Count); }, TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, SampleEngineEvent("a")).Should().BeTrue();
    pipeline.TryEnqueue(LogPriority.Normal, SampleEngineEvent("b")).Should().BeTrue();
    pipeline.FlushOnce().Should().BeTrue();

    written.Select(e => e.Message).Should().Equal("a", "b");
  }

  [Fact]
  public void FlushOnce_drains_high_priority_events_before_normal()
  {
    var written = new List<LogEvent>();
    var pipeline = new LogPipeline(batch => { written.AddRange(batch); return LogBatchWriteResult.Primary(batch.Count); }, TinyOptions());

    pipeline.TryEnqueue(LogPriority.Normal, SampleEngineEvent("normal-1"));
    pipeline.TryEnqueue(LogPriority.High, SampleEngineEvent("high-1", EventNameRegistry.IterationError));
    pipeline.TryEnqueue(LogPriority.Normal, SampleEngineEvent("normal-2"));
    pipeline.FlushOnce();

    written.Select(e => e.Message).Should().Equal("high-1", "normal-1", "normal-2");
  }

  [Fact]
  public void Configuration_mutation_event_carries_component_channel_and_event_name()
  {
    var logEvent = LogStructuredSink.CreateComponentEvent(
      LogSeverity.Info,
      LogComponentRegistry.Configuration,
      LogChannelRegistry.Policy,
      EventNameRegistry.ConfigMutationLogged,
      "paused");

    logEvent.Component.Should().Be(LogComponentRegistry.Configuration);
    logEvent.Channel.Should().Be(LogChannelRegistry.Policy);
    logEvent.EventName.Should().Be(EventNameRegistry.ConfigMutationLogged);
  }

  [Fact]
  public void Policy_channel_maps_from_config_mutation_event_name()
  {
    LogEventChannels.ForEventName(EventNameRegistry.ConfigMutationLogged)
      .Should().Be(LogChannelRegistry.Policy);
  }

  [Fact]
  public void TryWriteBatch_persists_deserializable_jsonl_lines()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
    var path = Path.Combine(dir, "structured.log");
    var logEvent = SampleEngineEvent("structured line");
    try
    {
      LogStructuredSink.TryWriteBatch(path, new[] { logEvent }, retention: null).Should().BeTrue();
      var line = File.ReadAllLines(path).Single(l => !string.IsNullOrWhiteSpace(l));
      LogJsonlFormatter.TryParseEvent(line, out var parsed).Should().BeTrue();
      parsed!.Message.Should().Be("structured line");
      parsed.Component.Should().Be(LogComponentRegistry.Engine);
      parsed.Channel.Should().Be(LogChannelRegistry.Lifecycle);
    }
    finally
    {
      if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
  }

  [Fact]
  public void TryAppendLines_mixed_with_legacy_text_and_new_jsonl()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "mixed.log");
    var retention = new LogRetentionPolicy(1_048_576, 7, 30, 104_857_600);
    try
    {
      File.WriteAllText(path, "[INFO] 2026-01-01 00:00:00 legacy line" + Environment.NewLine);
      var logEvent = SampleEngineEvent("json line");
      LogStructuredSink.TryWriteBatch(path, new[] { logEvent }, retention).Should().BeTrue();

      var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
      lines.Should().HaveCount(2);
      lines[0].Should().Contain("legacy line");
      LogJsonlFormatter.TryParseEvent(lines[1], out var parsed).Should().BeTrue();
      parsed!.Message.Should().Be("json line");
    }
    finally
    {
      if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
  }

  [Fact]
  public void TryAppendLines_with_retention_returns_false_when_mutex_unavailable()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.GetFullPath(Path.Combine(dir, "locked.log"));
    var retention = new LogRetentionPolicy(100, 7, 30, 104_857_600);
    using var mutex = new Mutex(true, SmartGuardLogWriter.MutexNameForTests(path));
    try
    {
      var result = false;
      var writer = Task.Run(() =>
        result = SmartGuardLogWriter.TryAppendLines(path, new[] { "{\"x\":1}" }, retention));
      writer.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
      result.Should().BeFalse("configured log with retention must fail closed when mutex unavailable");
    }
    finally
    {
      if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
  }

  [Fact]
  public void Three_size_rotations_on_same_day_create_sequenced_archives()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "rotate.log");
    var now = new DateTime(2026, 8, 3, 12, 0, 0);
    var retention = new LogRetentionPolicy(50, 7, 30, 104_857_600);
    try
    {
      for (var i = 0; i < 3; i++)
      {
        File.WriteAllText(path, new string('x', 100));
        SmartGuardLogWriter.TryAppendLines(path, new[] { $"{{\"i\":{i}}}" }, retention, now).Should().BeTrue();
      }

      File.Exists(path + ".20260803.001.bak").Should().BeTrue();
      File.Exists(path + ".20260803.002.bak").Should().BeTrue();
      File.Exists(path + ".20260803.003.bak").Should().BeTrue();
      LogArchivePlanner.TryParseArchiveIdentity(path + ".20260803.002.bak", out _, out var seq).Should().BeTrue();
      seq.Should().Be(2);
    }
    finally
    {
      if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
  }

  [Fact]
  public async Task StopAsync_drains_typed_event_queue_and_rejects_new_events()
  {
    var written = new List<LogEvent>();
    var pipeline = new LogPipeline(
      batch => { lock (written) written.AddRange(batch); return LogBatchWriteResult.Primary(batch.Count); }, TinyOptions());
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, SampleEngineEvent("pending-1"));
    pipeline.TryEnqueue(LogPriority.High, SampleEngineEvent("pending-2", EventNameRegistry.IterationError));

    (await pipeline.StopAsync()).Should().BeTrue();
    pipeline.State.Should().Be(LogPipelineState.Stopped);
    lock (written) written.Select(e => e.Message).Should().Contain(new[] { "pending-1", "pending-2" });
    pipeline.TryEnqueue(LogPriority.Normal, SampleEngineEvent("late")).Should().BeFalse();
    pipeline.Diagnostics.DroppedNormal.Should().Be(1);
  }
}
