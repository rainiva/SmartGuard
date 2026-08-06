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
        xaml.Should().Contain("AutomationProperties.Name=\"日志列表\"");
        xaml.Should().Contain("AutomationProperties.LiveSetting=\"Polite\"");
        xaml.Should().Contain("KeyboardNavigation.TabNavigation=\"Local\"");
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
}
