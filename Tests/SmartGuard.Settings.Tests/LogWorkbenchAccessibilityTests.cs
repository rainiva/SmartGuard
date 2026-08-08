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
        xaml.Should().Contain("AutomationProperties.Name=\"运行会话\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"选择全部日志或某次运行会话\"");
        xaml.Should().Contain("AutomationProperties.Name=\"日志列表\"");
        xaml.Should().Contain("AutomationProperties.LiveSetting=\"Polite\"");
        xaml.Should().Contain("KeyboardNavigation.TabNavigation=\"Local\"");
    }

    [Fact]
    public void Log_toolbar_keeps_search_actions_session_and_time_keyboard_reachable_in_visual_order()
    {
        var xaml = File.ReadAllText(
            SettingsSourcePaths.ProjectSettingsXamlPath(),
            Encoding.UTF8);

        var searchIndex = RequireIndex(xaml, "x:Name=\"logSearchFilterHost\"");
        var refreshIndex = RequireIndex(xaml, "x:Name=\"btnLogRefresh\"");
        var moreIndex = RequireIndex(xaml, "x:Name=\"btnLogMore\"");
        var sessionIndex = RequireIndex(xaml, "x:Name=\"cmbLogSession\"");
        var timeIndex = RequireIndex(xaml, "x:Name=\"cmbLogTimeRange\"");

        searchIndex.Should().BeLessThan(refreshIndex);
        refreshIndex.Should().BeLessThan(moreIndex);
        timeIndex.Should().BeLessThan(searchIndex);
        moreIndex.Should().BeLessThan(sessionIndex);

        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogSession\"[\\s\\S]{0,600}KeyboardNavigation\\.IsTabStop=\"True\"|x:Name=\"cmbLogSession\"[\\s\\S]{0,600}Focusable=\"True\"",
            "the top session picker should be explicitly keyboard reachable");
        xaml.Should().MatchRegex(
            "x:Name=\"cmbLogTimeRange\"[\\s\\S]{0,600}KeyboardNavigation\\.IsTabStop=\"True\"|x:Name=\"cmbLogTimeRange\"[\\s\\S]{0,600}Focusable=\"True\"",
            "the time picker should remain keyboard reachable after moving the session scope row");
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

    private static int RequireIndex(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"{value} should exist in Settings XAML");
        return index;
    }
}
