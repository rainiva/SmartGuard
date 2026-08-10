using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ConfigFileWatcherArchitectureTests
{
  [Fact]
  public void Config_change_detection_must_match_the_owner_lifecycle()
  {
    var repository = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardConfigRepository.cs");
    repository.Should().NotContain("ConfigFileWatcher.Watch");
    repository.Should().Contain("File.GetLastWriteTimeUtc");
    repository.Should().NotContain("private static FileSystemWatcher? CreateConfigWatcher");

    var trayCache = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayDisplaySettingsCache.cs");
    trayCache.Should().Contain("ConfigFileWatcher.Watch");
    trayCache.Should().NotContain("private static FileSystemWatcher? CreateConfigWatcher");
  }
}
