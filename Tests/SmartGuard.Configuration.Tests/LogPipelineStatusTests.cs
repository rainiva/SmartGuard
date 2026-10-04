using System.Text.Json;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogPipelineStatusTests
{
  [Fact]
  public async Task Current_health_recovers_without_erasing_historical_losses_or_drops()
  {
    var mode = 0;
    var pipeline = new LogPipeline(batch => Volatile.Read(ref mode) switch
    {
      1 => new LogBatchWriteResult(0, batch.Count, 0, true),
      2 => LogBatchWriteResult.Failed(batch.Count),
      _ => LogBatchWriteResult.Primary(batch.Count),
    });
    var sample = typeof(LogPipeline).GetMethod("SampleLogHealth");
    sample.Should().NotBeNull();
    JsonElement Read() => JsonSerializer.SerializeToElement(sample!.Invoke(pipeline, [true]));
    Read().GetProperty("code").GetString().Should().Be("unknown");
    pipeline.Start();
    try
    {
      async Task Write()
      {
        pipeline.TryEnqueue(LogPriority.Normal, LogEventFactory.Create(LogSeverity.Info,
          LogComponentRegistry.Engine, LogChannelRegistry.Lifecycle, EventNameRegistry.AppStarted, "sample"));
        await pipeline.FlushAsync();
      }
      await Write();
      Read().GetProperty("code").GetString().Should().Be("healthy");
      pipeline.RecordBackpressureDrop();
      Read().GetProperty("code").GetString().Should().Be("degraded");
      Read().GetProperty("code").GetString().Should().Be("healthy");
      Volatile.Write(ref mode, 1); await Write();
      Read().GetProperty("code").GetString().Should().Be("degraded");
      Volatile.Write(ref mode, 2); await Write();
      Read().GetProperty("code").GetString().Should().Be("failed");
      Volatile.Write(ref mode, 0); await Write();
      var recovered = Read();
      recovered.GetProperty("code").GetString().Should().Be("healthy");
      recovered.GetProperty("lostLines").GetInt64().Should().Be(1);
      recovered.GetProperty("droppedNormal").GetInt64().Should().Be(1);
    }
    finally { await pipeline.StopAsync(); }
  }
}
