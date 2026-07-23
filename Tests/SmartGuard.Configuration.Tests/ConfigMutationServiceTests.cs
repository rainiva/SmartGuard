using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class ConfigMutationServiceTests
{
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
      log.Should().Contain("暂停守护已开启");
      log.Should().Contain("暂停守护已关闭");
      File.Exists(SmartGuardPaths.StartupLogFile(dir)).Should().BeFalse(
        "pause/resume must land in the settings log page file, not startup.log");
    }
    finally
    {
      Directory.Delete(dir, true);
    }
  }
}
