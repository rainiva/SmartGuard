using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class GuardConfigMigrationParityTests
{
  [Fact]
  public void TryLoad_applies_notification_migration_when_external_change_key_missing()
  {
    var dir = Path.Combine(Path.GetTempPath(), "sg-migrate-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, SmartGuardPaths.ConfigFileName);

    try
    {
      File.WriteAllText(path, """
        {
          "NotifyOnPlanChange": true
        }
        """);

      var repository = new GuardConfigRepository(path);
      var config = repository.TryLoad();
      config.Should().NotBeNull();
      config!.NotifyOnExternalChange.Should().BeTrue();
    }
    finally
    {
      Directory.Delete(dir, true);
    }
  }
}
