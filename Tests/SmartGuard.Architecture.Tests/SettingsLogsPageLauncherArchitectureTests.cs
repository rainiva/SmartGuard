using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsLogsPageLauncherArchitectureTests
{
  [Fact]
  public void Settings_logs_page_launcher_must_be_single_shared_entry()
  {
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Configuration",
      "SettingsLogsPageLauncher.cs")).Should().BeTrue();

    var launcher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SettingsLogsPageLauncher.cs");
    launcher.Should().Contain("SettingsLogsPageLauncher");
    launcher.Should().Contain("SingleInstanceActivation.TryNotifyExisting");
    launcher.Should().Contain("--page logs");
  }

  [Fact]
  public void Tray_and_cli_must_delegate_to_SettingsLogsPageLauncher_without_legacy_LogViewer_entrypoints()
  {
    var tray = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Infrastructure.cs");
    tray.Should().NotContain("OpenLogViewer");
    var openLogsPage = ExtractOpenLogsPageMethod(tray);
    openLogsPage.Should().Contain("SettingsLogsPageLauncher");
    openLogsPage.Should().NotContain("Process.Start");

    var cli = SourceScanHelper.ReadSource("src/SmartGuard.Cli/CliOpenHandlers.cs");
    cli.Should().Contain("SettingsLogsPageLauncher.Open(root)");
    cli.Should().NotContain("SmartGuard.LogViewer");
  }

  private static string ExtractOpenLogsPageMethod(string source)
  {
    const string marker = "public static void OpenLogsPage";
    var start = source.IndexOf(marker, StringComparison.Ordinal);
    start.Should().BeGreaterThanOrEqualTo(0);
    var brace = source.IndexOf('{', start);
    var depth = 0;
    for (var i = brace; i < source.Length; i++)
    {
      if (source[i] == '{') depth++;
      else if (source[i] == '}')
      {
        depth--;
        if (depth == 0)
          return source[start..(i + 1)];
      }
    }

    throw new InvalidOperationException("Could not extract OpenLogsPage method");
  }
}
