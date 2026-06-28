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
    [InlineData("navDisplay", "显示")]
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
    public void Display_page_includes_theme_follow_system_controls()
    {
        var xaml = File.ReadAllText(RepoXamlPath());
        xaml.Should().Contain("x:Name=\"pageDisplay\"");
        xaml.Should().Contain("x:Name=\"rowThemeFollowSystem\"");
        xaml.Should().Contain("x:Name=\"tglThemeFollowSystem\"");
        xaml.Should().Contain("x:Name=\"rowThemeLight\"");
        xaml.Should().Contain("x:Name=\"tglThemeLight\"");
        xaml.Should().Contain("x:Name=\"tglThemeDark\"");
        xaml.Should().Contain("x:Name=\"rowThemeDark\"");
        xaml.Should().Contain("Text=\"浅色模式\"");
        xaml.Should().Contain("Text=\"使用浅色界面配色\"");
        xaml.Should().Contain("Text=\"使用深色界面配色\"");
        xaml.Should().NotContain("关闭跟随系统后可手动切换界面配色");
        xaml.Should().NotContain("x:Name=\"btnThemeToggle\"");
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
