namespace SmartGuard.Configuration.Tests;

using System.Text.Json;

public class GuardConfigRepositoryCacheTests
{
  [Fact]
  public void TryLoad_sees_values_after_external_file_rewrite()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardRepoWatcher_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      repo.Save(config);
      repo.TryLoad().Should().NotBeNull();

      repo.ResetMetricsForTests();
      var external = GuardConfig.CreateDefault(dir);
      external.Paused = true;
      File.WriteAllText(path, JsonSerializer.Serialize(external, GuardConfig.JsonOptions));
      File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));

      repo.TryLoad()!.Paused.Should().BeTrue();
      repo.DiskReadCountForTests.Should().BeGreaterThan(0);
    }
    finally
    {
      try { Directory.Delete(dir, true); } catch { }
    }
  }
}
