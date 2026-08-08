using System.IO;
namespace SmartGuard.Settings.Tests;

public class SettingsContentLayoutTests
{
    [Fact]
    public void Right_content_root_uses_shared_margin_like_logs_page()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().Contain("x:Key=\"SettingsContentPageMargin\"");
        xaml.Should().MatchRegex(
            "x:Name=\"rightContentRoot\"[\\s\\S]{0,260}Margin=\"\\{StaticResource SettingsContentPageMargin\\}\"",
            "settings pages should inherit the same outer margin as the logs page");
        xaml.Should().NotMatchRegex(
            "x:Name=\"pageLogs\"[\\s\\S]{0,260}Margin=\"\\{StaticResource SettingsContentPageMargin\\}\"",
            "logs page should not double-apply the outer margin");
    }

    [Fact]
    public void Page_titles_use_shared_header_row_without_duplicate_sidebar_brand_heading()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

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
        xaml.Should().NotMatchRegex(
            "<!-- Left Navigation Panel -->[\\s\\S]{0,260}RowDefinition Height=\"Auto\"",
            "the sidebar should no longer reserve a dedicated brand header row");
        xaml.Should().MatchRegex(
            "x:Name=\"navList\"[\\s\\S]{0,220}Margin=\"8,16,8,0\"",
            "navigation should keep a light top inset after removing the duplicate brand heading");
    }

    [Fact]
    public void Sidebar_does_not_repeat_product_name_that_is_already_in_the_window_title()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().NotContain("x:Name=\"imgAppIcon\"");
        xaml.Should().NotMatchRegex(
            "<!-- Left Navigation Panel -->[\\s\\S]{0,420}Text=\"智能电源守护\"[\\s\\S]{0,120}FontSize=\"18\"",
            "the sidebar should rely on the window title and page title instead of repeating the product name");
    }

    [Fact]
    public void Content_scroll_viewer_uses_content_style_with_visible_vertical_bar()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();
        xaml.Should().Contain("x:Key=\"SettingsContentScrollViewer\"");
        xaml.Should().MatchRegex(
            "x:Key=\"SettingsContentScrollViewer\"[\\s\\S]{0,260}VerticalScrollBarVisibility\" Value=\"Visible\"",
            "content scroll viewer should keep the vertical scrollbar visible to avoid layout reflow while scrolling");
        xaml.Should().MatchRegex(
            "ScrollContentPresenter[\\s\\S]{0,120}Margin=\"\\{TemplateBinding Padding\\}\"",
            "scroll content presenter should honor padding inside the scroll viewer template");
    }

    [Fact]
    public void Single_row_system_card_does_not_show_trailing_divider()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().MatchRegex(
            "Text=\"管理开机启动和基本系统行为\"[\\s\\S]{0,120}Border Style=\"\\{StaticResource SettingsItemRow\\}\" BorderThickness=\"0\"",
            "the only row in the system card should not render a bottom divider");
    }

    [Fact]
    public void Log_viewport_border_wraps_scroll_viewer_not_log_content()
    {
        var xaml = SettingsSourcePaths.ReadSettingsXamlSource();

        xaml.Should().MatchRegex(
            "x:Name=\"logViewportBorder\"[\\s\\S]{0,420}x:Name=\"logTable\"",
            "log viewport border should wrap the paged table so the frame stays fixed");
        xaml.Should().Contain("VirtualizingPanel.IsVirtualizing=\"True\"");
    }
}
