namespace SmartGuard.Configuration.Tests;

[Collection("ConfigAtomicWriteTests")]
public class SettingsSaveCoordinatorTests
{
  [Fact]
  public void Changing_only_observation_duration_logs_saved_settings_once()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(dir));
      var previous = GuardConfig.CreateDefault(dir);
      var updated = GuardConfig.LoadFromJson(System.Text.Json.JsonSerializer.Serialize(previous));
      updated.ForegroundObservationSec = 240;
      SettingsSaveCoordinator.Save(updated, previous, dir, repository);
      File.Exists(updated.LogFile).Should().BeTrue();
      File.ReadAllText(updated.LogFile).Should().Contain("240 秒").And.NotContain("已生效");
      SettingsSaveCoordinator.Save(updated, updated, dir, repository);
      File.ReadAllLines(updated.LogFile).Should().ContainSingle();
    }
    finally { Directory.Delete(dir, true); }
  }

  [Fact]
  public void Logging_path_preparation_failure_cannot_undo_a_saved_configuration()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var repo = new GuardConfigRepository(SmartGuardPaths.ConfigFile(dir));
    var previous = GuardConfig.CreateDefault(dir);
    repo.Save(previous);
    var updated = GuardConfig.LoadFromJson(System.Text.Json.JsonSerializer.Serialize(previous));
    updated.Paused = true;
    updated.LogFile = "bad\0path";
    try
    {
      Action save = () => SettingsSaveCoordinator.Save(updated, previous, dir, repo);
      save.Should().NotThrow("logging preparation is a best-effort post-commit side effect");
      repo.TryLoad()!.Paused.Should().BeTrue();
      Action append = () => repo.AppendInfoLog("saved", dir);
      append.Should().NotThrow("path resolution must also be within the best-effort boundary");
    }
    finally { Directory.Delete(dir, true); }
  }

  [Fact]
  public void Auto_start_failure_is_reported_as_post_commit_and_keeps_success_log()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var repo = new GuardConfigRepository(SmartGuardPaths.ConfigFile(dir));
    var previous = GuardConfig.CreateDefault(dir);
    repo.Save(previous);
    var updated = GuardConfig.LoadFromJson(System.Text.Json.JsonSerializer.Serialize(previous));
    updated.Paused = true;
    updated.AutoStartEnabled = !previous.AutoStartEnabled;
    var seam = typeof(SettingsSaveCoordinator).GetProperty("AutoStartOverrideForTests",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    try
    {
      seam.Should().NotBeNull("the isolated test must never change real scheduled tasks");
      seam!.SetValue(null, new Action<bool, string>((_, _) => throw new IOException("task update failed")));
      Action save = () => SettingsSaveCoordinator.Save(updated, previous, dir, repo);
      save.Should().Throw<InvalidOperationException>().WithMessage("*配置已保存*开机自启*");
      repo.TryLoad()!.Paused.Should().BeTrue();
      var log = File.ReadAllText(SmartGuardPaths.DefaultLogFile(dir));
      log.Should().Contain("ConfigMutationLogged");
      log.Should().NotContain("ConfigMutationFailed");
    }
    finally
    {
      seam?.SetValue(null, null);
      Directory.Delete(dir, true);
    }
  }

  [Fact]
  public void Failed_save_records_failure_only_and_reloads_previous_policy()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var repo = new GuardConfigRepository(SmartGuardPaths.ConfigFile(dir));
    repo.Save(GuardConfig.CreateDefault(dir));
    var previous = repo.TryLoad()!;
    var updated = GuardConfig.LoadFromJson(System.Text.Json.JsonSerializer.Serialize(previous));
    updated.Paused = true;
    try
    {
      GuardConfigAtomicFileWriter.BeforeMoveForTests = (_, _) => throw new IOException("save-failed");
      Action save = () => SettingsSaveCoordinator.Save(updated, previous, dir, repo);
      save.Should().Throw<IOException>();
      repo.TryLoad()!.Paused.Should().BeFalse();
      var log = File.ReadAllText(SmartGuardPaths.DefaultLogFile(dir));
      log.Should().NotContain("ConfigMutationLogged");
      log.Should().Contain("ConfigMutationFailed");
    }
    finally
    {
      GuardConfigAtomicFileWriter.BeforeMoveForTests = null;
      Directory.Delete(dir, true);
    }
  }

  [Fact]
  public void Successful_save_logs_to_new_path_and_same_count_rule_replacement_is_visible()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var repo = new GuardConfigRepository(SmartGuardPaths.ConfigFile(dir));
    var previous = GuardConfig.CreateDefault(dir);
    previous.ForegroundRestrictionEnabled = true;
    previous.ForegroundProgramPaths = [@"C:\Windows\a.exe"];
    repo.Save(previous);
    var updated = GuardConfig.LoadFromJson(System.Text.Json.JsonSerializer.Serialize(previous));
    updated.LogFile = Path.Combine(dir, "new.log");
    updated.ForegroundProgramPaths = [@"C:\Windows\b.exe"];
    try
    {
      SettingsSaveCoordinator.Save(updated, previous, dir, repo);
      File.Exists(updated.LogFile).Should().BeTrue();
      File.ReadAllText(updated.LogFile).Should().Contain("程序规则已保存");
      File.ReadAllText(updated.LogFile).Should().NotContain(@"C:\Windows");
      SettingsSaveCoordinator.Save(updated, updated, dir, repo);
      File.ReadAllLines(updated.LogFile).Should().ContainSingle();
    }
    finally { Directory.Delete(dir, true); }
  }

  [Fact]
  public void Save_writes_config_and_preserves_unknown_keys()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.config.json");
    File.WriteAllText(path, """{"BalancedThresholdSec":300,"PowerSaverThresholdSec":900,"CustomFlag":true}""");

    try
    {
      var repo = new GuardConfigRepository(path);
      var previous = repo.TryLoad()!;
      var updated = SettingsSnapshotMapper.ApplyTraySettings(
        previous, 5, 15, 30, 15, 300, previous.HeartbeatIntervalMin,
        previous.ActivePlanGuid, previous.BalancedPlanGuid, previous.PowerSaverPlanGuid,
        false, true, true, true);

      SettingsSaveCoordinator.Save(updated, previous, dir, repo);

      var text = File.ReadAllText(path);
      text.Should().Contain("CustomFlag");
      text.Should().Contain("\"CheckIntervalSec\": 15");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }
}
