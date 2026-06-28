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

            File.WriteAllText(path, JsonSerializer.Serialize(new StatusPayload { currentPlan = "节能" }));
            store.Read()!.currentPlan.Should().Be("节能");

            store.DiskReadCountForTests.Should().Be(2);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
