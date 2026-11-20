using System.Text.Json;
using SmartGuard.Contracts;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class StatusStoreCacheTests
{
    [Fact]
    public void Read_reuses_cache_when_file_is_unchanged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardStatusStore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.status.json");
        var payload = new StatusPayload
        {
            currentPlan = "平衡",
            batteryPercent = 80,
            isOnAC = true,
            brightness = 50,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload));
        // Only a write older than the settle window is served from memory (FileReadStamp).
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromMinutes(1));

        try
        {
            var store = new StatusStore(path);
            store.Read().Should().NotBeNull();
            store.Read().Should().NotBeNull();

            store.DiskReadCountForTests.Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_reloads_after_file_changes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardStatusStore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.status.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new StatusPayload { currentPlan = "平衡" }));

        try
        {
            var store = new StatusStore(path);
            store.Read()!.currentPlan.Should().Be("平衡");

            Thread.Sleep(100);
            File.WriteAllText(path, JsonSerializer.Serialize(new StatusPayload { currentPlan = "节能" }));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            StatusPayload? updated = null;
            while (DateTime.UtcNow < deadline)
            {
                updated = store.Read();
                if (updated?.currentPlan == "节能")
                    break;
                Thread.Sleep(50);
            }

            updated.Should().NotBeNull();
            updated!.currentPlan.Should().Be("节能");

            store.DiskReadCountForTests.Should().Be(2);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_rereads_a_rewrite_that_keeps_the_same_stamp_inside_the_settle_window()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardStatusStore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.status.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new StatusPayload { currentPlan = "平衡", paused = false }));

        try
        {
            var store = new StatusStore(path);
            store.Read()!.paused.Should().BeFalse();
            var stamp = File.GetLastWriteTimeUtc(path);

            // Same length ("false" -> " true") and the same stamp, as a coarse file clock produces.
            File.WriteAllText(path, JsonSerializer.Serialize(new StatusPayload { currentPlan = "平衡", paused = true }) + " ");
            File.SetLastWriteTimeUtc(path, stamp);
            new FileInfo(path).Length.Should().Be(JsonSerializer.Serialize(new StatusPayload { currentPlan = "平衡", paused = false }).Length);

            store.Read()!.paused.Should().BeTrue("a write inside the settle window cannot be trusted by its stamp");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
