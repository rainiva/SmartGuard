using System.IO;
using System.Reflection;

namespace SmartGuard.Settings.Tests;

public class SettingsNavigationUiTests
{
    private static string RepoXamlPath()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml");
    }

    [Theory]
    [InlineData("navGeneral", "常规")]
    [InlineData("navAdvanced", "高级")]
    [InlineData("navNotifications", "通知")]
    [InlineData("navLogs", "日志")]
    [InlineData("navAbout", "关于")]
    public void Navigation_item_includes_mdl2_icon_before_label(string navName, string label)
    {
        var xaml = File.ReadAllText(RepoXamlPath());
        var start = xaml.IndexOf($"x:Name=\"{navName}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);

        var end = xaml.IndexOf("</ListBoxItem>", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        var itemXaml = xaml[start..end];
        itemXaml.Should().Contain("FontFamily=\"Segoe MDL2 Assets\"");
        itemXaml.Should().Contain($"Text=\"{label}\"");
    }

    [Fact]
    public void Theme_toggle_includes_mdl2_icon_before_label()
    {
        var xaml = File.ReadAllText(RepoXamlPath());
        xaml.Should().Contain("x:Name=\"iconTheme\"");
        xaml.Should().Contain("x:Name=\"txtTheme\"");
        xaml.Should().MatchRegex(
            "btnThemeToggle[\\s\\S]*iconTheme[\\s\\S]*Segoe MDL2 Assets[\\s\\S]*txtTheme",
            "theme toggle should show glyph before text");
    }

    [Fact]
    public void Log_search_row_includes_search_glyph_before_textbox()
    {
        var xaml = File.ReadAllText(RepoXamlPath());
        xaml.Should().MatchRegex(
            "logSearchFilterHost[\\s\\S]{0,400}Segoe MDL2 Assets|Segoe MDL2 Assets[\\s\\S]{0,400}logSearchFilterHost",
            "log search should include a search glyph near the text box");
    }
}
