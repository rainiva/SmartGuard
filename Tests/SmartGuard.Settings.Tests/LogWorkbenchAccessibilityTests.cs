using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogWorkbenchAccessibilityTests
{
    [Fact]
    public void Log_page_declares_names_help_text_and_live_status_contract()
    {
        var xaml = File.ReadAllText(
            SettingsSourcePaths.ProjectSettingsXamlPath(),
            Encoding.UTF8);

        xaml.Should().Contain("AutomationProperties.Name=\"日志查询\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"输入关键词或组合查询\"");
        xaml.Should().Contain("AutomationProperties.Name=\"日志级别\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"选择全部级别或指定级别\"");
        xaml.Should().Contain("AutomationProperties.Name=\"日志来源\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"选择全部来源或指定来源\"");
        xaml.Should().Contain("AutomationProperties.Name=\"日志列表\"");
        xaml.Should().Contain("AutomationProperties.LiveSetting=\"Polite\"");
        xaml.Should().Contain("KeyboardNavigation.TabNavigation=\"Local\"");
    }

    [Fact]
    public void Log_toolbar_keeps_search_actions_time_level_and_source_keyboard_reachable_in_visual_order()
    {
        var xaml = File.ReadAllText(
            SettingsSourcePaths.ProjectSettingsXamlPath(),
            Encoding.UTF8);

        var searchIndex = RequireIndex(xaml, "x:Name=\"logSearchFilterHost\"");
        var refreshIndex = RequireIndex(xaml, "x:Name=\"btnLogRefresh\"");
        var filterIndex = RequireIndex(xaml, "x:Name=\"btnLogFilter\"");
        var moreIndex = RequireIndex(xaml, "x:Name=\"btnLogMore\"");
        var timeIndex = RequireIndex(xaml, "x:Name=\"cmbLogTimeRange\"");
        var levelIndex = RequireIndex(xaml, "x:Name=\"cmbLogLevel\"");
        var sourceIndex = RequireIndex(xaml, "x:Name=\"cmbLogSource\"");
        var sessionIndex = RequireIndex(xaml, "x:Name=\"cmbLogSession\"");

        searchIndex.Should().BeLessThan(refreshIndex);
        refreshIndex.Should().BeLessThan(filterIndex);
        filterIndex.Should().BeLessThan(moreIndex);
        moreIndex.Should().BeLessThan(timeIndex);
        timeIndex.Should().BeLessThan(levelIndex);
        levelIndex.Should().BeLessThan(sourceIndex);
        sourceIndex.Should().BeLessThan(sessionIndex);

        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogLevel\"[\\s\\S]{0,600}KeyboardNavigation\\.IsTabStop=\"True\"|x:Name=\"cmbLogLevel\"[\\s\\S]{0,600}Focusable=\"True\"",
            "the level picker should be explicitly keyboard reachable");
        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSource\"[\\s\\S]{0,600}KeyboardNavigation\\.IsTabStop=\"True\"|x:Name=\"cmbLogSource\"[\\s\\S]{0,600}Focusable=\"True\"",
            "the source picker should be explicitly keyboard reachable");
        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogTimeRange\"[\\s\\S]{0,600}KeyboardNavigation\\.IsTabStop=\"True\"|x:Name=\"cmbLogTimeRange\"[\\s\\S]{0,600}Focusable=\"True\"",
            "the time picker should remain keyboard reachable inside the filter popup");
        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSession\"[\\s\\S]{0,300}Visibility=\"Collapsed\"",
            "the internal session state control must not enter the visible keyboard flow");
    }

    [Fact]
    public void Log_list_is_keyboard_reachable_and_has_a_visible_automation_name()
    {
        WpfStaTestHost.Run(() =>
        {
            var list = new ListBox();

            LogViewListPresenter.ConfigureListBox(list);

            KeyboardNavigation.GetIsTabStop(list).Should().BeTrue();
            AutomationProperties.GetName(list).Should().Be("日志列表");
            list.FocusVisualStyle.Should().NotBeNull();
        });
    }

    [Fact]
    public void Paged_log_table_and_pager_are_keyboard_reachable_and_named()
    {
        var xaml = File.ReadAllText(
            SettingsSourcePaths.ProjectSettingsXamlPath(),
            Encoding.UTF8);

        xaml.Should().Contain("x:Name=\"logTable\"");
        xaml.Should().Contain("AutomationProperties.Name=\"日志表格\"");
        xaml.Should().Contain("KeyboardNavigation.IsTabStop=\"True\"");
        xaml.Should().Contain("x:Name=\"btnLogFirstPage\"");
        xaml.Should().Contain("x:Name=\"btnLogPreviousPage\"");
        xaml.Should().Contain("x:Name=\"logPageNumberItems\"");
        xaml.Should().Contain("x:Name=\"btnLogNextPage\"");
        xaml.Should().Contain("x:Name=\"btnLogLastPage\"");
    }

    [Fact]
    public void Log_table_help_text_names_exactly_the_four_visible_columns()
    {
        var xaml = File.ReadAllText(
            SettingsSourcePaths.ProjectSettingsXamlPath(),
            Encoding.UTF8);

        xaml.Should().Contain(
            "AutomationProperties.HelpText=\"按级别、时间、来源和事件查看当前页日志\"",
            "级别、时间、来源、事件 are the only visible columns the table should advertise");
        xaml.Should().NotContain("按级别、时间、来源、事件和状态查看当前页日志");
    }

    [Fact]
    public void Search_bar_can_move_keyboard_focus_to_the_keyword_input()
    {
        WpfStaTestHost.Run(() =>
        {
            var bar = new LogSearchFilterBar();
            var window = new Window { Content = bar, Width = 320, Height = 80 };

            WpfStaTestHost.ShowAndWait(window);
            bar.FocusKeywordInput().Should().BeTrue();

            Keyboard.FocusedElement.Should().BeSameAs(
                bar.Children.OfType<TextBox>().Single());
        });
    }

    [Fact]
    public void Filter_popup_scroll_container_keeps_time_level_and_source_keyboard_reachable()
    {
        var xaml = File.ReadAllText(
            SettingsSourcePaths.ProjectSettingsXamlPath(),
            Encoding.UTF8);

        var panelIndex = RequireIndex(xaml, "x:Name=\"logFilterPanel\"");
        var scrollIndex = xaml.IndexOf("<ScrollViewer", panelIndex, StringComparison.Ordinal);
        scrollIndex.Should().BeGreaterThan(panelIndex,
            "the filter popup content must live inside a scroll container so a height-constrained popup stays keyboard reachable");
        var scrollCloseIndex = xaml.IndexOf("</ScrollViewer>", scrollIndex, StringComparison.Ordinal);
        scrollCloseIndex.Should().BeGreaterThan(scrollIndex);

        var scrollSlice = xaml[scrollIndex..scrollCloseIndex];
        scrollSlice.Should().Contain("VerticalScrollBarVisibility=\"Auto\"",
            "the constrained popup must offer a visible scroll affordance instead of clipping content");
        scrollSlice.Should().Contain("HorizontalScrollBarVisibility=\"Disabled\"",
            "the popup must not introduce horizontal scrolling");

        var timeIndex = scrollSlice.IndexOf("x:Name=\"cmbLogTimeRange\"", StringComparison.Ordinal);
        var levelIndex = scrollSlice.IndexOf("x:Name=\"cmbLogLevel\"", StringComparison.Ordinal);
        var sourceIndex = scrollSlice.IndexOf("x:Name=\"cmbLogSource\"", StringComparison.Ordinal);
        timeIndex.Should().BeGreaterThan(-1, "the time picker must stay inside the scroll container");
        levelIndex.Should().BeGreaterThan(-1, "the level picker must stay inside the scroll container");
        sourceIndex.Should().BeGreaterThan(-1, "the source picker must stay inside the scroll container");
        timeIndex.Should().BeLessThan(levelIndex);
        levelIndex.Should().BeLessThan(sourceIndex,
            "Tab order inside the popup must run time -> level -> source in visual order");

        foreach (var controlName in new[] { "cmbLogTimeRange", "cmbLogLevel", "cmbLogSource" })
        {
            scrollSlice.Should().MatchRegex(
                $"x:Name=\"{controlName}\"[\\s\\S]{{0,400}}KeyboardNavigation\\.IsTabStop=\"True\"|KeyboardNavigation\\.IsTabStop=\"True\"[\\s\\S]{{0,400}}x:Name=\"{controlName}\"",
                $"{controlName} must stay explicitly keyboard reachable inside the popup scroll container");
        }
    }

    private static int RequireIndex(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"{value} should exist in Settings XAML");
        return index;
    }
}
