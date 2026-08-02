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
  public async Task StartHealthReporting_emits_timestamped_diag_line_after_interval()
  {
    var writtenLines = new System.Collections.Concurrent.ConcurrentQueue<string>();
    var pipeline = new LogPipeline(lines =>
    {
      foreach (var line in lines)
        writtenLines.Enqueue(line);
      return true;
    });
    pipeline.Start();

    var health = new LogPipelineHealth();
    pipeline.StartHealthReporting(health, TimeSpan.FromMilliseconds(100));

    // 等待至少 2 个周期（200ms）+ 余量
    await Task.Delay(1000);

    pipeline.StopHealthReporting();
    pipeline.StopAsync().GetAwaiter().GetResult();

    // 健康记录必须使用标准日志包头，才能按时间排序和筛选。
    pipeline.Diagnostics.WrittenLines.Should().BeGreaterThanOrEqualTo(2);
    writtenLines.Should().Contain(line =>
      System.Text.RegularExpressions.Regex.IsMatch(
        line,
        "^\\[DIAG\\] \\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2} \\{\\\"health\\\":\\\"LogPipeline\\\""));
  }

  [Fact]
  public async Task StopHealthReporting_halts_emission()
  {
    var pipeline = new LogPipeline(_ => true);
    pipeline.Start();

    var health = new LogPipelineHealth();
    pipeline.StartHealthReporting(health, TimeSpan.FromMilliseconds(100));
    await Task.Delay(300); // 等待若干周期

    var enqueuedBeforeStop = pipeline.Diagnostics.Enqueued;
    pipeline.StopHealthReporting();

    await Task.Delay(500); // 停止后再等待

    // 落盘计数可能在停止后继续排空停止前的队列；发射计数才是定时器是否仍在产生日志的口径。
    var enqueuedAfterStop = pipeline.Diagnostics.Enqueued;
    (enqueuedAfterStop - enqueuedBeforeStop).Should().BeLessThanOrEqualTo(1);

    pipeline.StopAsync().GetAwaiter().GetResult();
  }
}
