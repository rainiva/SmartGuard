using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractScheduledTaskIsolationTests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Contract_must_register_scheduled_task_install_skip_gate()
  {
    ContractContent.Should().Contain("ScheduledTaskInstallGate");
    ContractContent.Should().Contain("SMARTGUARD_SKIP_SCHEDULED_TASKS");
    ContractContent.Should().Contain("--skip-scheduled-tasks");
    ContractContent.Should().Contain("RegisterGuardian");
    ContractContent.Should().Contain("RegisterTray");
    ContractContent.Should().Contain("DesktopShellRegistration.Apply");
  }

  [Fact]
  public void Contract_must_register_integration_capture_restore_rule()
  {
    ContractContent.Should().Contain("Capture-SmartGuardScheduledTasks");
    ContractContent.Should().Contain("Restore-SmartGuardScheduledTasks");
    ContractContent.Should().MatchRegex("Capture.*Stop|Stop.*Capture", "Capture must occur before Stop in integration tests");
  }

  [Fact]
  public void Section6_must_index_scheduled_task_isolation_gates()
  {
    var section6 = ExtractSection(ContractContent, "## 6.");
    section6.Should().Contain("InstallCommandsSkipArchitectureTests");
    section6.Should().Contain("ArchitectureContractScheduledTaskIsolationTests");
  }

  [Fact]
  public void Integration_helpers_must_implement_capture_restore_and_default_skip()
  {
    var restore = SourceScanHelper.ReadSource("Tests/Integration/Restore-SmartGuardScheduledTasks.ps1");
    restore.Should().Contain("function Capture-SmartGuardScheduledTasks");
    restore.Should().Contain("function Restore-SmartGuardScheduledTasks");

    var helpers = SourceScanHelper.ReadSource("Tests/Integration/InstallerUserFlow.Helpers.ps1");
    helpers.Should().Contain("SMARTGUARD_SKIP_SCHEDULED_TASKS");
    helpers.Should().Contain("$SkipScheduledTasks = $true");
  }

  private static string ExtractSection(string content, string header)
  {
    var start = content.IndexOf(header, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1);
    var next = content.IndexOf("\n## ", start + header.Length, StringComparison.Ordinal);
    return next > start ? content[start..next] : content[start..];
  }
}
