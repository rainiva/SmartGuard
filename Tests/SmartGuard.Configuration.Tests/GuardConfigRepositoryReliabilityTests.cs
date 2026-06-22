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
}
