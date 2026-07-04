using System.IO;

namespace SmartGuard.Settings.Tests;

public class LogScrollBarStyleTests
{
    [Fact]
    public void Log_list_uses_settings_scroll_viewer_template()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().Contain("x:Key=\"SettingsLogListBox\"");
        xaml.Should().MatchRegex(
            "x:Name=\"lstLogView\"[\\s\\S]{0,220}Style=\"\\{StaticResource SettingsLogListBox\\}\"",
            "log list should reuse the styled scroll viewer template");
        xaml.Should().MatchRegex(
            "x:Key=\"SettingsLogListBox\"[\\s\\S]{0,700}Style=\"\\{StaticResource SettingsScrollViewer\\}\"",
            "log list template should host the settings scroll viewer style");
    }
}
