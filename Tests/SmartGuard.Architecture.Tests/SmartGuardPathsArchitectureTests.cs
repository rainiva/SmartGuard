using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SmartGuardPathsArchitectureTests
{
  [Fact]
  public void Settings_must_not_hardcode_default_log_path_under_install_root()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsWindowController.cs");
    source.Should().NotContain(
      "Path.Combine(root, \"SmartGuard.log\")",
      "use SmartGuardPaths and config.LogFile");
  }
}
