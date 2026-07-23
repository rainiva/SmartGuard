using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class InstallerRestartArchitectureTests
{
  [Fact]
  public void Installer_must_not_schedule_reboot_via_restartreplace_or_run_side_effects()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var writer = SourceScanHelper.ReadSource(
      "src/SmartGuard.Packaging/Inno/InnoDistributionManifestWriter.cs");

    writer.Should().NotContain(
      "restartreplace",
      "locked bin files must not be deferred to reboot; stop processes then overwrite");
    iss.Should().Contain("RestartIfNeededByRun=no");
    iss.Should().Contain("NeedsRestart := False");
  }
}
