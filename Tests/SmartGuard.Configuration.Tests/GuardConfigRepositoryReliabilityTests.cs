namespace SmartGuard.Configuration.Tests;

public class GuardConfigRepositoryReliabilityTests
{
    [Fact]
    public void TryLoad_reflects_external_file_changes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardRepoReliability_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");

        try
        {
            var repo = new GuardConfigRepository(path);
            var config = GuardConfig.CreateDefault(dir);
            repo.Save(config);
            repo.TryLoad()!.Paused.Should().BeFalse();

            var json = File.ReadAllText(path).Replace("\"Paused\": false", "\"Paused\": true", StringComparison.Ordinal);
            File.WriteAllText(path, json);

            repo.TryLoad()!.Paused.Should().BeTrue("external config edits must not be masked by the read cache");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void TryLoad_rereads_a_rewrite_that_keeps_the_same_stamp_inside_the_settle_window()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardRepoReliability_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");

        try
        {
            var repo = new GuardConfigRepository(path);
            repo.Save(GuardConfig.CreateDefault(dir));
            repo.TryLoad()!.LowBatteryPercent.Should().Be(30);
            var stamp = File.GetLastWriteTimeUtc(path);

            // A coarse file clock gives a same-length rewrite inside one tick the very same stamp.
            var json = File.ReadAllText(path).Replace("\"LowBatteryPercent\": 30", "\"LowBatteryPercent\": 31", StringComparison.Ordinal);
            File.WriteAllText(path, json);
            File.SetLastWriteTimeUtc(path, stamp);

            repo.TryLoad()!.LowBatteryPercent.Should().Be(31, "a write inside the settle window cannot be trusted by its stamp");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void TryLoad_reads_nothing_for_a_settled_unchanged_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardRepoReliability_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");

        try
        {
            var repo = new GuardConfigRepository(path);
            repo.Save(GuardConfig.CreateDefault(dir));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromMinutes(1));
            repo.TryLoad().Should().NotBeNull();

            repo.ResetMetricsForTests();
            repo.TryLoad();
            repo.TryLoad();
            repo.DiskReadCountForTests.Should().Be(0, "a settled unchanged file is served from memory");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
