using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ConfigFileWatcherArchitectureTests
{
  [Fact]
  public void Config_change_detection_must_match_the_owner_lifecycle()
  {
    var repository = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardConfigRepository.cs");
    repository.Should().NotContain("ConfigFileWatcher.Watch");
    // On-demand change detection goes through the single stamp rule (write time + length, trusted only
    // once settled); a bare write-time comparison misses same-tick edits on coarse file clocks.
    repository.Should().Contain("FileReadStamp.TryCapture").And.NotContain("File.GetLastWriteTimeUtc");
    SourceScanHelper.ReadSource("src/SmartGuard.Tray/StatusStore.cs")
      .Should().Contain("FileReadStamp.TryCapture").And.NotContain("File.GetLastWriteTimeUtc");
    repository.Should().NotContain("private static FileSystemWatcher? CreateConfigWatcher");

    var trayCache = SourceScanHelper.ReadSource("src/SmartGuard.Tray/TrayDisplaySettingsCache.cs");
    trayCache.Should().Contain("ConfigFileWatcher.Watch");
    trayCache.Should().NotContain("private static FileSystemWatcher? CreateConfigWatcher");
  }
}
