using System.Reflection;
using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

[CollectionDefinition("LoggingFallbackTests", DisableParallelization = true)]
public class LoggingFallbackCollection { }

[Collection("LoggingFallbackTests")]
public class GuardianLoggingFailureTests
{
  [Fact]
  public void Primary_degradation_and_restoration_emit_one_transition_each()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-log-transition-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var blocked = Path.Combine(root, "directory");
      Directory.CreateDirectory(blocked);
      var config = GuardConfig.CreateDefault(root);
      config.LogFile = blocked;
      var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(root));
      repository.Save(config);
      var loop = new GuardianLoop(root, SmartGuardPaths.ConfigFile(root), SmartGuardPaths.StatusFile(root),
        SmartGuardPaths.InitMarkerFile(root), SmartGuardPaths.StartupLogFile(root), false);
      var consume = typeof(GuardianLoop).GetMethod("WriteConsumerBatch", BindingFlags.NonPublic | BindingFlags.Instance);
      consume.Should().NotBeNull("the consumer must report recovery transitions through bounded admission");
      var pipeline = (LogPipeline)typeof(GuardianLoop).GetField("_logPipeline", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loop)!;
      var batch = new[] { EngineLogEvents.Create(LogLevel.Info, EventNameRegistry.IterationError, "isolated") };
      consume!.Invoke(loop, [batch]);
      consume.Invoke(loop, [batch]);
      pipeline.Diagnostics.Enqueued.Should().Be(1);
      config.LogFile = Path.Combine(root, "main.log");
      repository.Save(config);
      consume.Invoke(loop, [batch]);
      consume.Invoke(loop, [batch]);
      pipeline.Diagnostics.Enqueued.Should().Be(2);
      pipeline.FlushOnce().Should().BeTrue();
      var events = File.ReadLines(config.LogFile).Select(line =>
      {
        LogJsonlFormatter.TryParseLine(line, out var item).Should().BeTrue();
        return item!;
      }).ToArray();
      events.Count(e => e.EventName == EventNameRegistry.RecoveryDegraded).Should().Be(1);
      events.Count(e => e.EventName == EventNameRegistry.RecoveryRestored).Should().Be(1);
    }
    finally { Directory.Delete(root, true); }
  }

  [Theory]
  [InlineData(LogLevel.Warn, LogPriority.High, 2_000)]
  [InlineData(LogLevel.Info, LogPriority.Normal, 16_000)]
  public void Business_rejection_never_attempts_disk_and_counts_once(LogLevel level, LogPriority priority, int depth)
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-log-reject-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var loop = new GuardianLoop(root, SmartGuardPaths.ConfigFile(root), SmartGuardPaths.StatusFile(root),
        SmartGuardPaths.InitMarkerFile(root), SmartGuardPaths.StartupLogFile(root), false);
      var pipeline = (LogPipeline)typeof(GuardianLoop).GetField("_logPipeline", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loop)!;
      for (var i = 0; i < depth; i++)
        pipeline.TryEnqueue(priority, EngineLogEvents.Create(level, EventNameRegistry.IterationError, "queued"));
      var result = (GuardianLogWriteResult)typeof(GuardianLoop).GetMethod("WriteLog", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(loop, [GuardConfig.CreateDefault(root), level, "rejected", EventNameRegistry.IterationError, null])!;
      result.Should().Be(priority == LogPriority.High ? GuardianLogWriteResult.Rejected : GuardianLogWriteResult.DroppedByBackpressure);
      Directory.EnumerateFiles(root).Should().BeEmpty("daily producers must never fall back to disk");
      (priority == LogPriority.High ? pipeline.Diagnostics.DroppedHigh : pipeline.Diagnostics.DroppedNormal).Should().Be(1);
    }
    finally { Directory.Delete(root, true); }
  }
}
