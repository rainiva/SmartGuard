using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogDeduplicationFilterTests
{
  [Fact]
  public void First_occurrence_passes_through()
  {
    var filter = new LogDeduplicationFilter(threshold: 3);
    var accepted = filter.TryAccept("fp1", "hello world", out var output);
    accepted.Should().BeTrue();
    output.Should().Be("hello world");
  }

  [Fact]
  public void Immediate_duplicate_is_suppressed()
  {
    var filter = new LogDeduplicationFilter(threshold: 3);
    filter.TryAccept("fp1", "hello", out _);
    var accepted = filter.TryAccept("fp1", "hello", out var output);
    accepted.Should().BeFalse();
    output.Should().BeNull();
  }

  [Fact]
  public void Threshold_exceeded_emits_dedup_summary()
  {
    var filter = new LogDeduplicationFilter(threshold: 3);
    filter.TryAccept("fp1", "hello", out _); // 1st: pass
    filter.TryAccept("fp1", "hello", out _); // 2nd: suppress
    filter.TryAccept("fp1", "hello", out _); // 3rd: suppress
    var accepted = filter.TryAccept("fp1", "hello", out var output); // 4th: dedup summary
    accepted.Should().BeTrue();
    output.Should().StartWith("[DEDUP x3]");
    output.Should().Contain("hello");
  }

  [Fact]
  public void Different_fingerprint_resets_counter()
  {
    var filter = new LogDeduplicationFilter(threshold: 3);
    filter.TryAccept("fp1", "hello", out _);
    filter.TryAccept("fp1", "hello", out _);
    // 不同指纹出现：重置
    var accepted = filter.TryAccept("fp2", "world", out var output);
    accepted.Should().BeTrue();
    output.Should().Be("world");
  }

  [Fact]
  public void Dedup_resets_after_summary_emission()
  {
    var filter = new LogDeduplicationFilter(threshold: 2);
    filter.TryAccept("fp1", "msg", out _); // 1st
    filter.TryAccept("fp1", "msg", out _); // 2nd: suppress
    filter.TryAccept("fp1", "msg", out var summary); // 3rd: dedup + reset
    summary.Should().StartWith("[DEDUP x2]");
    // 重新计数
    var accepted = filter.TryAccept("fp1", "msg", out var next);
    accepted.Should().BeTrue();
    next.Should().Be("msg");
  }

  [Fact]
  public void Reset_clears_all_state()
  {
    var filter = new LogDeduplicationFilter(threshold: 3);
    filter.TryAccept("fp1", "hello", out _);
    filter.TryAccept("fp1", "hello", out _);
    filter.Reset();
    var accepted = filter.TryAccept("fp1", "hello", out var output);
    accepted.Should().BeTrue();
    output.Should().Be("hello");
  }
}
