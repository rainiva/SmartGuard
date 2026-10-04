using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("ConfigAtomicWriteTests")]
public class ConfigMutationServiceTests
{
  [Fact]
  public void Failed_pause_save_preserves_file_and_cached_policy_without_success_event()
  {
    var dir = Path.Combine(Path.GetTempPath(), "sg-mutation-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(dir));
    repository.Save(GuardConfig.CreateDefault(dir));
    var loaded = repository.TryLoad()!;
    var failure = new IOException("publication failed");
    try
    {
      GuardConfigAtomicFileWriter.BeforeMoveForTests = (_, _) => throw failure;
      Action mutate = () => new ConfigMutationService(repository).SetPaused(true, dir, SmartGuardPaths.StartupLogFile(dir));
      mutate.Should().Throw<IOException>().Which.Should().BeSameAs(failure);
      new GuardConfigRepository(SmartGuardPaths.ConfigFile(dir)).TryLoad()!.Paused.Should().BeFalse();
      repository.TryLoad()!.Paused.Should().BeFalse("failed persistence must not authorize the cached new policy");
      loaded.Paused.Should().BeFalse("mutation must use a copy of the loaded policy");
      var log = File.ReadAllText(SmartGuardPaths.DefaultLogFile(dir));
      log.Should().NotContain("ConfigMutationLogged");
      log.Should().Contain("ConfigMutationFailed");
      log.Should().Contain("配置未提交，变更未保存");
    }
    finally
    {
      GuardConfigAtomicFileWriter.BeforeMoveForTests = null;
      Directory.Delete(dir, true);
    }
  }

  [Fact]
  public void Repeated_pause_without_change_writes_one_persisted_success()
  {
    var dir = Path.Combine(Path.GetTempPath(), "sg-mutation-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(dir));
    repository.Save(GuardConfig.CreateDefault(dir));
    try
    {
      var service = new ConfigMutationService(repository);
      service.SetPaused(true, dir, SmartGuardPaths.StartupLogFile(dir));
      service.SetPaused(true, dir, SmartGuardPaths.StartupLogFile(dir));
      File.ReadAllLines(SmartGuardPaths.DefaultLogFile(dir)).Should().ContainSingle();
      File.ReadAllText(SmartGuardPaths.DefaultLogFile(dir)).Should().Contain("已保存");
    }
    finally { Directory.Delete(dir, true); }
  }

  [Fact]
  public void SetPaused_persists_through_repository_save()
  {
    var dir = Path.Combine(Path.GetTempPath(), "sg-mutation-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, SmartGuardPaths.ConfigFileName);
    var repository = new GuardConfigRepository(path);
    repository.Save(GuardConfig.CreateDefault(dir));
    var mutations = new ConfigMutationService(repository);

    try
    {
      mutations.SetPaused(true, dir, SmartGuardPaths.StartupLogFile(dir));
      repository.TryLoad()!.Paused.Should().BeTrue();
    }
    finally
    {
      Directory.Delete(dir, true);
    }
  }

  [Fact]
  public void SetPaused_writes_explicit_pause_and_resume_lines_to_default_log_file()
  {
    var dir = Path.Combine(Path.GetTempPath(), "sg-mutation-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, SmartGuardPaths.ConfigFileName);
    var logPath = SmartGuardPaths.DefaultLogFile(dir);
    var repository = new GuardConfigRepository(path);
    repository.Save(GuardConfig.CreateDefault(dir));
    var mutations = new ConfigMutationService(repository);

    try
    {
      mutations.SetPaused(true, dir, SmartGuardPaths.StartupLogFile(dir));
      mutations.SetPaused(false, dir, SmartGuardPaths.StartupLogFile(dir));

      var log = File.ReadAllText(logPath);
      log.Should().Contain("暂停守护选项已开启");
      log.Should().Contain("暂停守护选项已关闭");
      File.Exists(SmartGuardPaths.StartupLogFile(dir)).Should().BeFalse(
        "pause/resume must land in the settings log page file, not startup.log");
    }
    finally
    {
      Directory.Delete(dir, true);
    }
  }
}
