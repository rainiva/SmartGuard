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
  public void LogViewer_and_Tray_must_delegate_to_SettingsLogsPageLauncher()
  {
    var redirect = SourceScanHelper.ReadSource("src/SmartGuard.LogViewer/LogViewerEntryRedirect.cs");
    redirect.Should().Contain("SettingsLogsPageLauncher");
    redirect.Should().NotContain("Process.Start");

    var tray = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Infrastructure.cs");
    var openLogViewer = ExtractOpenLogViewerMethod(tray);
    openLogViewer.Should().Contain("SettingsLogsPageLauncher");
    openLogViewer.Should().NotContain("Process.Start");
  }

  private static string ExtractOpenLogViewerMethod(string source)
  {
    const string marker = "public static void OpenLogViewer";
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

    throw new InvalidOperationException("Could not extract OpenLogViewer method");
  }
}
