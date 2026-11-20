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

  [Fact]
  public void Failed_activation_keeps_the_manual_boost_and_says_the_guardian_takes_over()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(GuardConfig.CreateDefault(dir)));
    try
    {
      var repo = new GuardConfigRepository(path);
      var activator = new RecordingPowerPlanActivator { Result = new PowerSchemeStatus("api-error", 5, 1) };

      HighPerformanceBoost.Apply(repo, dir, activator, TimeSpan.FromMinutes(45));

      var saved = repo.TryLoad()!;
      saved.ManualHighPerformanceUntil.Should().NotBeNull("the guardian enforces the boost even if the tray switch failed");
      // AppendInfoLog writes the structured main log; the startup log is only its fallback.
      var log = File.ReadAllText(SmartGuardPaths.ResolveLogFile(saved, dir), System.Text.Encoding.UTF8);
      log.Should().Contain("立即切换未完成").And.Contain("错误码 5").And.Contain("守护程序将在下一轮切换");
    }
    finally { Directory.Delete(dir, recursive: true); }
  }

  private sealed class RecordingPowerPlanActivator : IPowerPlanActivator
  {
    public Guid? LastGuid { get; private set; }

    public PowerSchemeStatus Result { get; init; } = new("none", null, 0);

    public PowerSchemeStatus SetActivePlan(Guid planGuid)
    {
      LastGuid = planGuid;
      return Result;
    }
  }
}
