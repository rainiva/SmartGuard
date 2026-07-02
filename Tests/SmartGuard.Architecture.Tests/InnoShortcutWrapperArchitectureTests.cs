using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class InnoShortcutWrapperArchitectureTests
{
  [Fact]
  public void Inno_icons_must_not_spawn_settings_exe_directly()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var iconsSection = ExtractSection(iss, "[Icons]");
    iconsSection.Should().Contain("Open-Settings.cmd");
    iconsSection.Should().Contain("Open-Logs.cmd");
    iconsSection.Should().Contain("Open-Tray.cmd");
    iconsSection.Should().NotContain("bin\\SmartGuard.Settings.exe");
    iconsSection.Should().NotContain(@"{app}\bin\{#MyAppExeName}");
  }

  [Fact]
  public void Inno_Run_postinstall_must_use_Open_Tray_wrapper()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var runSection = ExtractSection(iss, "[Run]");
    runSection.Should().Contain("Open-Tray.cmd");
    runSection.Should().NotContain("{app}\\bin\\{#MyAppExeName}");
  }

  [Fact]
  public void Open_wrapper_cmds_must_match_launcher_arguments()
  {
    var openSettings = SourceScanHelper.ReadSource("Open-Settings.cmd");
    var openLogs = SourceScanHelper.ReadSource("Open-Logs.cmd");
    var mainLauncher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SettingsMainPageLauncher.cs");
    var logsLauncher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SettingsLogsPageLauncher.cs");

    openSettings.Should().Contain("--root \"%~dp0\"");
    openLogs.Should().Contain("--root \"%~dp0\" --page logs");
    mainLauncher.Should().Contain("Arguments = $\"--root \\\"{root}\\\"\"");
    logsLauncher.Should().Contain("Arguments = $\"--root \\\"{root}\\\" --page logs\"");
  }

  private static string ExtractSection(string iss, string header)
  {
    var start = iss.IndexOf(header, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1, $"expected section {header}");
    var next = iss.IndexOf("\r\n[", start + header.Length, StringComparison.Ordinal);
    if (next < 0)
      next = iss.IndexOf("\n[", start + header.Length, StringComparison.Ordinal);
    return next > start ? iss[start..next] : iss[start..];
  }
}
