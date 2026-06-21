using System.IO;
using System.Reflection;

namespace SmartGuard.Settings.Tests;

public class SettingsContentLayoutTests
{
    private static string RepoXamlPath()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var testProjectDir = Path.GetDirectoryName(assemblyLocation)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "lib", "SmartGuard.Settings.xaml");
    }

    [Fact]
    public void Right_content_root_uses_shared_margin_like_logs_page()
    {
        var xaml = File.ReadAllText(RepoXamlPath());

        xaml.Should().Contain("x:Key=\"SettingsContentPageMargin\"");
        xaml.Should().MatchRegex(
            "x:Name=\"rightContentRoot\"[\\s\\S]{0,260}Margin=\"\\{StaticResource SettingsContentPageMargin\\}\"",
            "settings pages should inherit the same outer margin as the logs page");
        xaml.Should().NotMatchRegex(
            "x:Name=\"pageLogs\"[\\s\\S]{0,260}Margin=\"\\{StaticResource SettingsContentPageMargin\\}\"",
            "logs page should not double-apply the outer margin");
    }

    [Fact]
    public void Page_titles_use_shared_header_row_aligned_with_navigation_brand()
    {
        var xaml = File.ReadAllText(RepoXamlPath());

        xaml.Should().Contain("x:Name=\"txtPageTitle\"");
        xaml.Should().MatchRegex(
            "x:Name=\"txtPageTitle\"[\\s\\S]{0,120}Grid\\.Row=\"0\"",
            "page title should sit in the fixed header row above scrollable content");
        xaml.Should().MatchRegex(
            "x:Name=\"contentScrollViewer\"[\\s\\S]{0,120}Grid\\.Row=\"1\"",
            "scrollable settings content should sit below the shared page title");
        xaml.Should().NotMatchRegex(
            "pageGeneral[\\s\\S]{0,160}Text=\"常规设置\"[\\s\\S]{0,80}SectionTitle",
            "page titles should not be duplicated inside scrollable page content");
        xaml.Should().Contain("<Thickness x:Key=\"SettingsContentPageMargin\">24,24,24,12</Thickness>");
        xaml.Should().MatchRegex(
            "Margin=\"20,24,20,16\"[\\s\\S]{0,200}imgAppIcon",
            "left app title block uses 24px top margin");
    }

    [Fact]
    public void Settings_scroll_viewer_template_honors_padding()
    {
        var xaml = File.ReadAllText(RepoXamlPath());
        xaml.Should().MatchRegex(
            "ScrollContentPresenter[\\s\\S]{0,120}Margin=\"\\{TemplateBinding Padding\\}\"",
            "scroll content presenter should honor padding inside the scroll viewer template");
    }

    [Fact]
    public void Single_row_system_card_does_not_show_trailing_divider()
    {
        var xaml = File.ReadAllText(RepoXamlPath());

        xaml.Should().MatchRegex(
            "Text=\"管理开机启动和基本系统行为\"[\\s\\S]{0,120}Border Style=\"\\{StaticResource SettingsItemRow\\}\" BorderThickness=\"0\"",
            "the only row in the system card should not render a bottom divider");
    }

    [Fact]
    public void Log_viewport_border_wraps_scroll_viewer_not_log_content()
    {
        var xaml = File.ReadAllText(RepoXamlPath());

        xaml.Should().MatchRegex(
            "x:Name=\"logViewportBorder\"[\\s\\S]{0,420}x:Name=\"logScrollViewer\"",
            "log viewport border should wrap the scroll viewer so the frame stays fixed");
        xaml.Should().NotMatchRegex(
            "x:Name=\"logScrollViewer\"[\\s\\S]{0,420}Border[\\s\\S]{0,420}x:Name=\"txtLogView\"",
            "log border must not scroll with log text");
    }
}
