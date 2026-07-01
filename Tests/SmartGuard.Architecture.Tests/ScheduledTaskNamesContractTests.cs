using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class ScheduledTaskNamesContractTests
{
  [Fact]
  public void Status_cmd_queries_registrar_task_names_only()
  {
    var content = File.ReadAllText(Path.Combine(SourceScanHelper.RepoRoot, "Status.cmd"));
    content.Should().Contain(ScheduledTaskRegistrar.GuardianTaskName);
    content.Should().Contain(ScheduledTaskRegistrar.TrayTaskName);
    content.Should().NotContain("SmartPowerPlan");
  }

  [Fact]
  public void Inno_setup_uses_registrar_guardian_task_name()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    iss.Should().Contain(ScheduledTaskRegistrar.GuardianTaskName);
    iss.Should().NotContain("SmartPowerPlan Guardian");
  }

  [Fact]
  public void Inno_Run_section_starts_guardian_via_registrar_task_name()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var runStart = iss.IndexOf("[Run]", StringComparison.Ordinal);
    runStart.Should().BeGreaterThan(-1);
    var runEnd = iss.IndexOf("\n[", runStart + 5, StringComparison.Ordinal);
    if (runEnd < 0) runEnd = iss.Length;
    var runSection = iss[runStart..runEnd];
    runSection.Should().Contain(ScheduledTaskRegistrar.GuardianTaskName);
    runSection.Should().MatchRegex(@"schtasks\.exe.*\/Run", "post-install start is Inno-specific schtasks /Run entry");
  }

  [Fact]
  public void Inno_stop_procedure_does_not_duplicate_schtasks_literals()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var start = iss.IndexOf("procedure StopSmartGuardProcesses", StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1);
    var stopBlock = iss[start..(iss.IndexOf("end;", start, StringComparison.Ordinal) + 4)];
    stopBlock.Should().NotContain("schtasks /End");
    stopBlock.Should().NotContain("schtasks /Delete");
    stopBlock.Should().Contain("--uninstall");
  }
}
