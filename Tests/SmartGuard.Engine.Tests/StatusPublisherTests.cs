using System.Text.Json;
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
    public void Publish_writes_engine_pid_to_status_json()
    {
        var publisher = new StatusPublisher(_statusPath);
        var payload = CreatePayload(idleSeconds: 10, batteryPercent: 80, Guid.NewGuid().ToString());
        payload.enginePid = Environment.ProcessId;

        publisher.Publish(payload);

        File.ReadAllText(_statusPath).Should().Contain($"\"enginePid\": {Environment.ProcessId}");
    }

    [Fact]
    public void Publish_skips_write_when_only_idle_seconds_increases()
    {
        var publisher = new StatusPublisher(_statusPath);
        var planGuid = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(idleSeconds: 10, batteryPercent: 80, planGuid));

        var firstJson = File.ReadAllText(_statusPath);

        publisher.Publish(CreatePayload(idleSeconds: 25, batteryPercent: 80, planGuid));

        File.ReadAllText(_statusPath).Should().Be(firstJson);
    }

    [Fact]
    public void Publish_writes_when_idle_seconds_drops_significantly()
    {
        var publisher = new StatusPublisher(_statusPath);
        var planGuid = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(idleSeconds: 500, batteryPercent: 80, planGuid));

        var firstJson = File.ReadAllText(_statusPath);

        publisher.Publish(CreatePayload(idleSeconds: 10, batteryPercent: 80, planGuid));

        File.ReadAllText(_statusPath).Should().NotBe(firstJson);
        File.ReadAllText(_statusPath).Should().Contain("\"idleSeconds\": 10");
    }

    [Fact]
    public void Publish_skips_write_when_idle_seconds_increase_is_small()
    {
        var publisher = new StatusPublisher(_statusPath);
        var planGuid = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(idleSeconds: 100, batteryPercent: 80, planGuid));

        var firstJson = File.ReadAllText(_statusPath);

        publisher.Publish(CreatePayload(idleSeconds: 115, batteryPercent: 80, planGuid));

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

    [Fact]
    public void Publish_skips_write_when_only_operation_id_changes()
    {
        var publisher = new StatusPublisher(_statusPath);
        var planGuid = Guid.NewGuid().ToString();
        var first = CreatePayload(idleSeconds: 10, batteryPercent: 80, planGuid);
        first.operationId = "op-iteration-1";
        publisher.Publish(first);

        var firstJson = File.ReadAllText(_statusPath);

        var second = CreatePayload(idleSeconds: 10, batteryPercent: 80, planGuid);
        second.operationId = "op-iteration-2";
        publisher.Publish(second);

        File.ReadAllText(_statusPath).Should().Be(firstJson,
            "operationId 每轮迭代都会 GenerateNew，若纳入去重比较将导致状态无实质变化仍每轮重写 status.json");
    }

    [Fact]
    public void Successful_replacement_retains_one_previous_snapshot()
    {
        var publisher = new StatusPublisher(_statusPath);
        var plan = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(10, 80, plan));
        var previous = File.ReadAllText(_statusPath);
        publisher.Publish(CreatePayload(10, 70, plan));

        File.ReadAllText(_statusPath + ".bak").Should().Be(previous);
        previous = File.ReadAllText(_statusPath);
        publisher.Publish(CreatePayload(10, 60, plan));
        File.ReadAllText(_statusPath + ".bak").Should().Be(previous);
        Directory.GetFiles(Path.GetDirectoryName(_statusPath)!, "*.bak").Should().HaveCount(1);
    }

    [Fact]
    public void Interrupted_replacement_backup_is_restored_before_a_failing_publish()
    {
        var old = JsonSerializer.Serialize(CreatePayload(10, 80, Guid.NewGuid().ToString()));
        File.WriteAllText(_statusPath + ".bak", old);
        Directory.CreateDirectory(_statusPath + ".tmp");
        var publisher = new StatusPublisher(_statusPath);

        Action publish = () => publisher.Publish(CreatePayload(10, 70, Guid.NewGuid().ToString()));
        publish.Should().Throw<Exception>();
        File.ReadAllText(_statusPath).Should().Be(old);
    }

    [Fact]
    public void Unchanged_status_is_refreshed_when_max_staleness_is_reached()
    {
        var time = new ManualTimeProvider();
        var publisher = new StatusPublisher(_statusPath, time, TimeSpan.FromSeconds(60));
        var payload = CreatePayload(idleSeconds: 0, batteryPercent: 90, Guid.NewGuid().ToString());

        publisher.Publish(payload);
        var firstWrite = File.GetLastWriteTimeUtc(_statusPath);

        time.Advance(TimeSpan.FromSeconds(59));
        publisher.Publish(payload);
        File.GetLastWriteTimeUtc(_statusPath).Should().Be(firstWrite);

        time.Advance(TimeSpan.FromSeconds(1));
        payload.timestamp = time.GetUtcNow().ToString("O");
        publisher.Publish(payload);
        using var document = JsonDocument.Parse(File.ReadAllText(_statusPath));
        document.RootElement.GetProperty("timestamp").GetString().Should().Be(payload.timestamp);
    }

    [Fact]
    public void Idle_growth_is_visible_at_refresh_cadence()
    {
        var time = new ManualTimeProvider();
        var publisher = new StatusPublisher(_statusPath, time, TimeSpan.FromSeconds(60));
        var planGuid = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(0, 90, planGuid));

        time.Advance(TimeSpan.FromSeconds(60));
        publisher.Publish(CreatePayload(60, 90, planGuid));

        File.ReadAllText(_statusPath).Should().Contain("\"idleSeconds\": 60");
    }

    [Fact]
    public void Publish_replaces_status_while_a_shared_snapshot_is_open()
    {
        var publisher = new StatusPublisher(_statusPath);
        var plan = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(0, 90, plan));
        using var snapshot = new FileStream(_statusPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        publisher.Publish(CreatePayload(60, 80, plan));

        SmartGuard.Configuration.StatusJsonReader.TryRead(_statusPath)!.batteryPercent.Should().Be(80);
        using var old = JsonDocument.Parse(snapshot);
        old.RootElement.GetProperty("batteryPercent").GetInt32().Should().Be(90);
    }

    [Fact]
    public void Rejected_status_replacement_preserves_old_bytes_and_can_be_retried()
    {
        var publisher = new StatusPublisher(_statusPath);
        var plan = Guid.NewGuid().ToString();
        publisher.Publish(CreatePayload(0, 90, plan));
        var original = File.ReadAllBytes(_statusPath);
        var update = CreatePayload(60, 80, plan);
        using (var held = new FileStream(_statusPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var publish = () => publisher.Publish(update);
            publish.Should().Throw<Exception>();
            File.ReadAllBytes(_statusPath).Should().Equal(original);
            File.Exists(_statusPath + ".tmp").Should().BeFalse();
        }

        publisher.Publish(update);
        SmartGuard.Configuration.StatusJsonReader.TryRead(_statusPath)!.batteryPercent.Should().Be(80);
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
