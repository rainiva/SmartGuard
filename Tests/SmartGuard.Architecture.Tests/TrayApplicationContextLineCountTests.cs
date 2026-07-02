using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class TrayApplicationContextLineCountTests
{
  [Fact]
  public void TrayApplicationContext_must_stay_under_300_lines()
  {
    var path = Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Tray", "TrayApplicationContext.cs");
    File.ReadAllLines(path).Length.Should().BeLessThan(300);
  }

  [Fact]
  public void Tray_context_menu_and_recovery_must_be_split_modules()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Tray", "TrayContextMenuFactory.cs"))
      .Should().BeTrue();
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Tray", "TrayGuardianRecoveryHandler.cs"))
      .Should().BeTrue();
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Tray", "TrayNotificationHelper.cs"))
      .Should().BeTrue();
  }
}
