using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsMainPageLauncherArchitectureTests
{
  [Fact]
  public void Settings_main_page_launcher_must_be_single_shared_entry()
  {
    File.Exists(Path.Combine(
      SourceScanHelper.RepoRoot,
      "src",
      "SmartGuard.Configuration",
      "SettingsMainPageLauncher.cs")).Should().BeTrue();

    var launcher = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SettingsMainPageLauncher.cs");
    launcher.Should().Contain("SettingsMainPageLauncher");
    launcher.Should().Contain("SingleInstanceActivation.TryNotifyExisting");
    launcher.Should().Contain("TryNotifyExisting(\"Settings\")");
    launcher.Should().NotContain("--page logs");
  }

  [Fact]
  public void Tray_must_delegate_OpenSettings_to_SettingsMainPageLauncher()
  {
    var tray = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Infrastructure.cs");
    var openSettings = ExtractOpenSettingsMethod(tray);
    openSettings.Should().Contain("SettingsMainPageLauncher");
    openSettings.Should().NotContain("Process.Start");
  }

  private static string ExtractOpenSettingsMethod(string source)
  {
    const string marker = "public static void OpenSettings";
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

    throw new InvalidOperationException("Could not extract OpenSettings method");
  }
}
