using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ConfigFileWatcherArchitectureTests
{
  [Fact]
  public void Config_watchers_must_use_shared_ConfigFileWatcher_helper()
  {
    var repository = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardConfigRepository.cs");
    repository.Should().Contain("ConfigFileWatcher.Watch");
    repository.Should().NotContain("private static FileSystemWatcher? CreateConfigWatcher");

    var trayCache = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayDisplaySettingsCache.cs");
    trayCache.Should().Contain("ConfigFileWatcher.Watch");
    trayCache.Should().NotContain("private static FileSystemWatcher? CreateConfigWatcher");
  }
}
