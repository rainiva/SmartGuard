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

  [Fact]
  public void Installer_stop_gate_must_use_EngineLifecycle_exit_code_instead_of_tasklist()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");

    iss.Should().NotContain(
      "tasklist",
      "tasklist can fail under resource pressure and must not make the installer skip EngineLifecycle");
    iss.Should().NotContain("SmartGuardProcessesStillRunning");
    iss.Should().Contain("Exec(EngineExe");
    iss.Should().Contain("(ResultCode = 0)");
  }
}
