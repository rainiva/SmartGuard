namespace SmartGuard.Configuration.Tests;

public class GuardConfigRepositoryReadCacheTests
{
    [Fact]
    public void TryLoad_reuses_memory_cache_when_file_is_unchanged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardRepoCache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");

        try
        {
            var repo = new GuardConfigRepository(path);
            repo.ResetMetricsForTests();
            var config = GuardConfig.CreateDefault(dir);
            repo.Save(config);
            // Only a write older than the settle window is served from memory (FileReadStamp).
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromMinutes(1));

            repo.TryLoad().Should().NotBeNull();
            repo.TryLoad().Should().NotBeNull();

            repo.DiskReadCountForTests.Should().Be(1);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void TryLoad_returns_fresh_values_after_save_and_reuses_cache_until_file_changes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardRepoCache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");

        try
        {
            var repo = new GuardConfigRepository(path);
            repo.ResetMetricsForTests();
            var config = GuardConfig.CreateDefault(dir);
            repo.Save(config);

            var loaded = repo.TryLoad()!;
            loaded.Paused = true;
            repo.Save(loaded);

            repo.TryLoad()!.Paused.Should().BeTrue();
            // Once the write is older than the settle window, the next read is trusted and later ones hit memory.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromMinutes(1));
            repo.TryLoad()!.Paused.Should().BeTrue();

            repo.ResetMetricsForTests();
            repo.TryLoad();
            repo.TryLoad();
            repo.DiskReadCountForTests.Should().Be(0);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
