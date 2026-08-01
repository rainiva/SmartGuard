using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogPipelineHealthTests
{
  [Fact]
  public void TakeSnapshot_captures_current_diagnostics()
  {
    var pipeline = new LogPipeline(_ => true);
    pipeline.Start();
    pipeline.TryEnqueue(LogPriority.Normal, "line1");
    pipeline.TryEnqueue(LogPriority.High, "line2");
    pipeline.FlushAsync().GetAwaiter().GetResult();

    var health = new LogPipelineHealth();
    var snapshot = health.TakeSnapshot(pipeline);

    snapshot.EnqueuedTotal.Should().BeGreaterThanOrEqualTo(2);
    snapshot.WrittenLinesTotal.Should().BeGreaterThanOrEqualTo(2);
    snapshot.DroppedNormal.Should().Be(0);
    snapshot.DroppedHigh.Should().Be(0);

    pipeline.StopAsync().GetAwaiter().GetResult();
  }

  [Fact]
  public void FormatJson_produces_valid_json_line()
  {
    var health = new LogPipelineHealth();
    var snapshot = new LogHealthSnapshot(
      EnqueuedTotal: 100,
      DroppedNormal: 5,
      DroppedHigh: 0,
      WrittenLinesTotal: 95,
      FailedBatches: 1,
      NormalQueueDepth: 10,
      HighQueueDepth: 0,
      EnqueueRatePerSec: 25.0,
      WriteRatePerSec: 23.5,
      AverageLatencyMs: 50.0,
      P99LatencyMs: 150.0);

    var json = health.FormatJson(snapshot);
    json.Should().NotBeNullOrEmpty();
    json.Should().Contain("\"enqueuedTotal\"");
    json.Should().Contain("\"droppedNormal\"");
    json.Should().Contain("\"p99LatencyMs\"");
    json.Should().NotContain("\n"); // 单行 JSON
  }

  [Fact]
  public void TakeSnapshot_computes_rates_from_two_snapshots()
  {
    var pipeline = new LogPipeline(_ => true);
    pipeline.Start();

    var health = new LogPipelineHealth();
    var snap1 = health.TakeSnapshot(pipeline);

    for (var i = 0; i < 10; i++)
      pipeline.TryEnqueue(LogPriority.Normal, $"line-{i}");
    pipeline.FlushAsync().GetAwaiter().GetResult();

    var snap2 = health.TakeSnapshot(pipeline);
    snap2.EnqueuedTotal.Should().BeGreaterThan(snap1.EnqueuedTotal);

    pipeline.StopAsync().GetAwaiter().GetResult();
  }

  [Fact]
  public async Task StartHealthReporting_emits_json_line_after_interval()
  {
    var pipeline = new LogPipeline(_ => true);
    pipeline.Start();

    var health = new LogPipelineHealth();
    pipeline.StartHealthReporting(health, TimeSpan.FromMilliseconds(100));

    // 等待至少 2 个周期（200ms）+ 余量
    await Task.Delay(1000);

    pipeline.StopHealthReporting();
    pipeline.StopAsync().GetAwaiter().GetResult();

    // 验证管线已入队健康 JSON 行（WrittenLines 至少包含 2 次健康报告）
    pipeline.Diagnostics.WrittenLines.Should().BeGreaterThanOrEqualTo(2);
  }

  [Fact]
  public async Task StopHealthReporting_halts_emission()
  {
    var pipeline = new LogPipeline(_ => true);
    pipeline.Start();

    var health = new LogPipelineHealth();
    pipeline.StartHealthReporting(health, TimeSpan.FromMilliseconds(100));
    await Task.Delay(300); // 等待若干周期

    var writtenBeforeStop = pipeline.Diagnostics.WrittenLines;
    pipeline.StopHealthReporting();

    await Task.Delay(500); // 停止后再等待

    // 停止后不应有新增写入（允许少量竞态余量，但不应持续增长）
    var writtenAfterStop = pipeline.Diagnostics.WrittenLines;
    (writtenAfterStop - writtenBeforeStop).Should().BeLessThanOrEqualTo(1);

    pipeline.StopAsync().GetAwaiter().GetResult();
  }
}
