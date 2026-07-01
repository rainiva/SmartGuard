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
}
