using FluentAssertions;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class GuardianIterationTimingTests
{
    [Fact]
    public void ResolveWaitSeconds_reads_latest_check_interval_from_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardLoopTiming_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");

        try
        {
            File.WriteAllText(path, """
                {
                  "CheckIntervalSec": 30
                }
                """);

            GuardianIterationTiming.ResolveWaitSeconds(path).Should().Be(30);

            File.WriteAllText(path, """
                {
                  "CheckIntervalSec": 5
                }
                """);

            GuardianIterationTiming.ResolveWaitSeconds(path).Should().Be(5);
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

        try
        {
            File.WriteAllText(path, """
                {
                  "CheckIntervalSec": 1
                }
                """);

            GuardianIterationTiming.ResolveWaitSeconds(path).Should().Be(5);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
