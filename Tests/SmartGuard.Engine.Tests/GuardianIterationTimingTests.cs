using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Tests;

public class GuardianIterationTimingTests
{
  [Fact]
  public void ResolveWaitSeconds_reads_latest_check_interval_from_disk()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardLoopTiming_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    var repository = new GuardConfigRepository(path);

    try
    {
      File.WriteAllText(path, """
          {
            "CheckIntervalSec": 30
          }
          """);

      GuardianIterationTiming.ResolveWaitSeconds(repository, dir).Should().Be(30);
      var firstWriteTimeUtc = File.GetLastWriteTimeUtc(path);

      File.WriteAllText(path, """
          {
            "CheckIntervalSec": 5
          }
          """);
      File.SetLastWriteTimeUtc(path, firstWriteTimeUtc.AddSeconds(1));

      GuardianIterationTiming.ResolveWaitSeconds(repository, dir).Should().Be(5);
    }
    finally
    {
      try { Directory.Delete(dir, true); } catch { }
    }
  }

  [Fact]
  public void ResolveWaitSeconds_enforces_minimum_of_five_seconds()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardLoopTimingMin_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    var repository = new GuardConfigRepository(path);

    try
    {
      File.WriteAllText(path, """
          {
            "CheckIntervalSec": 1
          }
          """);

      GuardianIterationTiming.ResolveWaitSeconds(repository, dir).Should().Be(5);
    }
    finally
    {
      try { Directory.Delete(dir, true); } catch { }
    }
  }

  [Fact]
  public void ResolveWaitSeconds_uses_the_shorter_lifecycle_deadline()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardLoopTimingLifecycle_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    var repository = new GuardConfigRepository(path);

    try
    {
      File.WriteAllText(path, """
          {
            "CheckIntervalSec": 30
          }
          """);

      GuardianIterationTiming.ResolveWaitSeconds(repository, dir, TimeSpan.FromSeconds(2))
        .Should().Be(2);
      GuardianIterationTiming.ResolveWaitSeconds(repository, dir, TimeSpan.FromMilliseconds(1))
        .Should().Be(1);
    }
    finally
    {
      try { Directory.Delete(dir, true); } catch { }
    }
  }
}
