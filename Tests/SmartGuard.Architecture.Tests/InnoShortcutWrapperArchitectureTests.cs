using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class InnoShortcutWrapperArchitectureTests
{
  [Fact]
  public void Inno_icons_must_use_Cli_open_not_spawn_settings_exe_directly()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var iconsSection = ExtractSection(iss, "[Icons]");
    iconsSection.Should().Contain("SmartGuard.Cli.exe");
    iconsSection.Should().Contain("open settings");
    iconsSection.Should().Contain("open logs");
    iconsSection.Should().Contain("open tray");
    iconsSection.Should().NotContain("Open-Settings.cmd");
    iconsSection.Should().NotContain("bin\\SmartGuard.Settings.exe");
    iconsSection.Should().NotContain(@"{app}\bin\{#MyAppExeName}");
  }

  [Fact]
  public void Inno_Run_postinstall_must_use_Cli_open_tray()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var runSection = ExtractSection(iss, "[Run]");
    runSection.Should().Contain("SmartGuard.Cli.exe");
    runSection.Should().Contain("open tray");
    runSection.Should().NotContain("Open-Tray.cmd");
    runSection.Should().NotContain("{app}\\bin\\{#MyAppExeName}");
  }

  [Fact]
  public void Cli_open_commands_must_delegate_to_launchers()
  {
    var router = string.Join(
      Environment.NewLine,
      Directory.EnumerateFiles(Path.Combine(SourceScanHelper.RepoRoot, "src", "SmartGuard.Cli"), "*.cs")
        .Select(File.ReadAllText));
    var mainLauncher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SettingsMainPageLauncher.cs");
    var logsLauncher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SettingsLogsPageLauncher.cs");

    router.Should().Contain("SettingsMainPageLauncher.Open");
    router.Should().Contain("SettingsLogsPageLauncher.Open");
    router.Should().Contain("TrayLauncher.Start");
    mainLauncher.Should().Contain("Arguments = $\"--root \\\"{root}\\\"\"");
    logsLauncher.Should().Contain("Arguments = $\"--root \\\"{root}\\\" --page logs\"");
  }

  [Fact]
  public void Open_wrapper_cmds_must_not_exist_at_repo_root()
  {
    foreach (var wrapper in new[] { "Open-Tray.cmd", "Open-Settings.cmd", "Open-Logs.cmd" })
    {
      File.Exists(Path.Combine(SourceScanHelper.RepoRoot, wrapper))
        .Should().BeFalse($"{wrapper} was replaced by SmartGuard.Cli.exe open");
    }
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
