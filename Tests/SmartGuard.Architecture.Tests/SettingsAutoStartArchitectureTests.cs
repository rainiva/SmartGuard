using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsAutoStartArchitectureTests
{
  [Fact]
  public void Settings_layer_must_not_call_SyncFromTasks()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src/SmartGuard.Settings"))
    {
      var relative = Path.GetRelativePath(SourceScanHelper.RepoRoot, file).Replace('\\', '/');
      if (relative.Contains("/obj/") || relative.Contains("/bin/"))
        continue;

      var source = File.ReadAllText(file);
      source.Should().NotContain(
        "SyncFromTasks",
        $"{relative} must read config.AutoStartEnabled only, not schtasks");
    }
  }

  [Fact]
  public void SettingsPolicyCoordinator_ApplyInitialValues_must_use_config_AutoStartEnabled()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    source.Should().MatchRegex(
      @"ApplyInitialValues[\s\S]*_tglAutoStart\.IsChecked\s*=\s*config\.AutoStartEnabled");
  }

  [Fact]
  public void AutoStartService_SetEnabled_only_from_SettingsSaveCoordinator()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      var relative = Path.GetRelativePath(SourceScanHelper.RepoRoot, file).Replace('\\', '/');
      if (relative.Contains("/obj/") || relative.Contains("/bin/"))
        continue;
      if (string.Equals(relative, "src/SmartGuard.Configuration/SettingsSaveCoordinator.cs", StringComparison.OrdinalIgnoreCase))
        continue;
      if (string.Equals(relative, "src/SmartGuard.Configuration/AutoStartService.cs", StringComparison.OrdinalIgnoreCase))
        continue;

      var source = File.ReadAllText(file);
      source.Should().NotContain(
        "AutoStartService.SetEnabled",
        $"{relative} must not call AutoStartService.SetEnabled; use SettingsSaveCoordinator.Save");
    }
  }
}
