namespace SmartGuard.Configuration.Tests;

public class HighPerformanceBoostTests
{
  [Fact]
  public void Apply_sets_manual_boost_until_and_activates_high_performance_plan()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    var config = GuardConfig.CreateDefault(dir);
    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(config));

    try
    {
      var repo = new GuardConfigRepository(path);
      var activator = new RecordingPowerPlanActivator();

      HighPerformanceBoost.Apply(repo, dir, activator, TimeSpan.FromMinutes(45));

      activator.LastGuid.Should().Be(config.ActivePlanGuid);
      var reloaded = repo.TryLoad();
      reloaded.Should().NotBeNull();
      reloaded!.ManualHighPerformanceUntil.Should().NotBeNull();
      reloaded.ManualHighPerformanceUntil!.Value.Should()
        .BeCloseTo(DateTime.Now.AddMinutes(45), TimeSpan.FromSeconds(5));
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  private sealed class RecordingPowerPlanActivator : IPowerPlanActivator
  {
    public Guid? LastGuid { get; private set; }

    public void SetActivePlan(Guid planGuid) => LastGuid = planGuid;
  }
}
