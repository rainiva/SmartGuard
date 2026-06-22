using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class StatusPublisherTests : IDisposable
{
    private readonly string _statusPath;

    public StatusPublisherTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Engine.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _statusPath = Path.Combine(dir, "SmartGuard.status.json");
    }

    public void Dispose()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statusPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    [Fact]
    public void Publish_skips_write_when_only_idle_seconds_changed()
    {
        var publisher = new StatusPublisher(_statusPath);
        var planGuid = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(idleSeconds: 10, batteryPercent: 80, planGuid));

        var firstJson = File.ReadAllText(_statusPath);

        publisher.Publish(CreatePayload(idleSeconds: 25, batteryPercent: 80, planGuid));

        File.ReadAllText(_statusPath).Should().Be(firstJson);
    }

    [Fact]
    public void Publish_writes_when_battery_percent_changes()
    {
        var publisher = new StatusPublisher(_statusPath);
        var planGuid = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(idleSeconds: 10, batteryPercent: 80, planGuid));

        var firstJson = File.ReadAllText(_statusPath);

        publisher.Publish(CreatePayload(idleSeconds: 10, batteryPercent: 72, planGuid));

        File.ReadAllText(_statusPath).Should().NotBe(firstJson);
    }

    private static StatusPayload CreatePayload(int idleSeconds, int batteryPercent, string planGuid)
    {
        return new StatusPayload
        {
            timestamp = DateTime.Now.ToString("s"),
            currentPlan = "平衡",
            currentPlanGUID = planGuid,
            expectedPlan = "平衡",
            idleSeconds = idleSeconds,
            isOnAC = true,
            batteryPercent = batteryPercent,
            brightness = 60,
            paused = false,
        };
    }
}
