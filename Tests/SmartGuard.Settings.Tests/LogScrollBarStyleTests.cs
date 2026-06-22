using System.IO;

namespace SmartGuard.Settings.Tests;

public class LogScrollBarStyleTests
{
    [Fact]
    public void Log_list_uses_settings_scroll_viewer_template()
    {
        var xaml = ReadSettingsXaml();

        xaml.Should().Contain("x:Key=\"SettingsLogListBox\"");
        xaml.Should().MatchRegex(
            "x:Name=\"lstLogView\"[\\s\\S]{0,220}Style=\"\\{StaticResource SettingsLogListBox\\}\"",
            "log list should reuse the same styled scroll viewer as settings pages");
        xaml.Should().MatchRegex(
            "x:Key=\"SettingsLogListBox\"[\\s\\S]{0,700}Style=\"\\{StaticResource SettingsScrollViewer\\}\"",
            "log list template should host the settings scroll viewer style");
    }

    private static string ReadSettingsXaml()
    {
        var assemblyLocation = typeof(SettingsWindowController).Assembly.Location;
        var repoRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(assemblyLocation)!,
            "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml"));
    }
}
