using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class CrashRingBufferTests
{
  [Fact]
  public void Add_stores_lines_up_to_capacity()
  {
    var buffer = new CrashRingBuffer(capacity: 3);
    buffer.Add("line1");
    buffer.Add("line2");
    buffer.Add("line3");

    var drained = buffer.Drain();
    drained.Should().HaveCount(3);
    drained.Should().ContainInOrder("line1", "line2", "line3");
  }

  [Fact]
  public void Add_evicts_oldest_when_capacity_exceeded()
  {
    var buffer = new CrashRingBuffer(capacity: 3);
    buffer.Add("line1");
    buffer.Add("line2");
    buffer.Add("line3");
    buffer.Add("line4");

    var drained = buffer.Drain();
    drained.Should().HaveCount(3);
    drained.Should().ContainInOrder("line2", "line3", "line4");
    drained.Should().NotContain("line1");
  }

  [Fact]
  public void Drain_returns_empty_when_buffer_is_empty()
  {
    var buffer = new CrashRingBuffer(capacity: 10);
    buffer.Drain().Should().BeEmpty();
  }

  [Fact]
  public void Add_is_thread_safe_under_concurrent_writers()
  {
    var buffer = new CrashRingBuffer(capacity: 1000);
    var tasks = Enumerable.Range(0, 4).Select(t =>
      Task.Run(() =>
      {
        for (var i = 0; i < 250; i++)
          buffer.Add($"t{t}-line{i}");
      })).ToArray();
    Task.WaitAll(tasks);

    var drained = buffer.Drain();
    drained.Should().HaveCount(1000);
  }

  [Fact]
  public void FlushToSync_writes_all_lines_to_disk()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "crash-ring.log");
    try
    {
      var buffer = new CrashRingBuffer(capacity: 10);
      buffer.Add("crash-line-1");
      buffer.Add("crash-line-2");

      var result = buffer.FlushToSync(path);
      result.Should().BeTrue();
      File.ReadAllLines(path).Should().ContainInOrder("crash-line-1", "crash-line-2");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void FlushToSync_returns_false_on_write_failure()
  {
    var buffer = new CrashRingBuffer(capacity: 10);
    buffer.Add("line");
    // 写入非法路径（目录不存在且无法创建）
    var result = buffer.FlushToSync(@"Z:\nonexistent\crash.log");
    result.Should().BeFalse();
  }

  [Fact]
  public void Instance_exposes_process_wide_singleton_and_crash_ring_log_path()
  {
    CrashRingBuffer.Instance.Should().NotBeNull();
    CrashRingBuffer.CrashRingLogFile.Should().EndWith("crash-ring.log");
  }
}
