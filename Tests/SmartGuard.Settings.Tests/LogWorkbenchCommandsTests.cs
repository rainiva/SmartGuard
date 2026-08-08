using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogWorkbenchCommandsTests
{
    [Theory]
    [InlineData(Key.F, ModifierKeys.Control)]
    [InlineData(Key.L, ModifierKeys.Control)]
    public void Search_gestures_resolve_to_focus_search(Key key, ModifierKeys modifiers)
    {
        LogWorkbenchCommands.TryResolve(
                key,
                modifiers,
                isTextInputFocused: true,
                out var command)
            .Should().BeTrue();

        command.Should().BeSameAs(LogWorkbenchCommands.FocusSearchCommand);
    }

    [Fact]
    public void Match_navigation_supports_forward_and_reverse_gestures()
    {
        Resolve(Key.F3, ModifierKeys.None, LogWorkbenchCommands.NavigateNextCommand);
        Resolve(Key.F3, ModifierKeys.Shift, LogWorkbenchCommands.NavigatePreviousCommand);
        Resolve(Key.Enter, ModifierKeys.None, LogWorkbenchCommands.NavigateNextCommand);
        Resolve(Key.Enter, ModifierKeys.Shift, LogWorkbenchCommands.NavigatePreviousCommand);
    }

    [Fact]
    public void Export_and_transient_layer_gestures_resolve_to_global_commands()
    {
        Resolve(Key.E, ModifierKeys.Control, LogWorkbenchCommands.ExportCommand);
        Resolve(Key.Escape, ModifierKeys.None, LogWorkbenchCommands.CloseTransientLayerCommand);
    }

    [Fact]
    public void Paged_actions_wire_filter_page_size_and_current_page_export()
    {
        var source = File.ReadAllText(
            Path.Combine(SettingsSourcePaths.RepoRoot(), "src/SmartGuard.Settings/SettingsLogPageHostWiring.cs"),
            System.Text.Encoding.UTF8);

        source.Should().Contain("WirePagedLogActions");
        source.Should().Contain("cmbLogPageSize");
        File.ReadAllText(
                Path.Combine(SettingsSourcePaths.RepoRoot(), "src/SmartGuard.Settings/SettingsLogPageHost.Refresh.cs"),
                System.Text.Encoding.UTF8)
            .Should().Contain("ExportCurrentPage");
    }

    [Fact]
    public void Restored_table_selection_waits_for_the_numbered_page_to_apply()
    {
        var source = File.ReadAllText(
            Path.Combine(SettingsSourcePaths.RepoRoot(), "src/SmartGuard.Settings/SettingsLogPageHost.Refresh.cs"),
            System.Text.Encoding.UTF8);

        source.Should().Contain("_pendingTableRowEventId");
        source.Should().Contain("ApplyPendingRestoredTableSelection");
        source.Should().Contain("var applyRestoredTableSelection = !model.IsProvisional");
        source.Should().Contain("if (applyRestoredTableSelection)");
    }

    [Fact]
    public void Home_end_gestures_distinguish_current_window_from_history()
    {
        Resolve(Key.Home, ModifierKeys.None, LogWorkbenchCommands.ScrollWindowTopCommand);
        Resolve(Key.End, ModifierKeys.None, LogWorkbenchCommands.ScrollWindowBottomCommand);
        Resolve(Key.Home, ModifierKeys.Control, LogWorkbenchCommands.ScrollHistoryStartCommand);
        Resolve(Key.End, ModifierKeys.Control, LogWorkbenchCommands.ScrollHistoryEndCommand);

        LogWorkbenchCommands.TryResolve(
                Key.Home,
                ModifierKeys.None,
                isTextInputFocused: true,
                out _)
            .Should().BeFalse();
        LogWorkbenchCommands.TryResolve(
                Key.End,
                ModifierKeys.Control,
                isTextInputFocused: true,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Unsupported_modifier_combinations_do_not_steal_text_input_commands()
    {
        LogWorkbenchCommands.TryResolve(
                Key.F,
                ModifierKeys.Alt,
                isTextInputFocused: false,
                out _)
            .Should().BeFalse();
        LogWorkbenchCommands.TryResolve(
                Key.Home,
                ModifierKeys.Shift,
                isTextInputFocused: false,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Wpf_window_routes_F3_to_the_unified_command_coordinator()
    {
        WpfStaTestHost.Run(() =>
        {
            var window = new Window { Width = 320, Height = 80 };
            var navigated = 0;

            WpfStaTestHost.ShowAndWait(window);
            SettingsLogPageHostWiring.WireKeyboardCommands(
                window,
                isLogPageActive: () => true,
                isTextInputFocused: () => false,
                focusSearch: () => { },
                navigateNext: () => navigated++,
                navigatePrevious: () => { },
                exportVisibleLog: () => { },
                scrollWindowTop: () => { },
                scrollWindowBottom: () => { },
                scrollHistoryStart: () => { },
                scrollHistoryEnd: () => { },
                closeTransientLayer: () => { });

            var keyEvent = new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window)!,
                0,
                Key.F3)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            window.RaiseEvent(keyEvent);

            navigated.Should().Be(1);
            keyEvent.Handled.Should().BeTrue();
        });
    }

    [Fact]
    public void Expanded_focused_combobox_keeps_escape_for_wpf_dropdown_handling()
    {
        WpfStaTestHost.Run(() =>
        {
            var comboBox = new ComboBox
            {
                ItemsSource = new[] { "全部会话", "当前会话" },
            };
            var window = new Window
            {
                Width = 320,
                Height = 80,
                Content = comboBox,
            };
            var transientLayerClosed = 0;

            WpfStaTestHost.ShowAndWait(window);
            comboBox.Focus();
            Keyboard.Focus(comboBox).Should().BeSameAs(comboBox);
            comboBox.IsDropDownOpen = true;
            WpfStaTestHost.WaitForRender();
            comboBox.IsDropDownOpen.Should().BeTrue();

            var windowPreviewObserved = false;
            object? windowPreviewSource = null;
            object? windowPreviewOriginalSource = null;
            window.AddHandler(
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler((_, e) =>
                {
                    windowPreviewObserved = true;
                    windowPreviewSource = e.Source;
                    windowPreviewOriginalSource = e.OriginalSource;
                }),
                handledEventsToo: true);

            SettingsLogPageHostWiring.WireKeyboardCommands(
                window,
                isLogPageActive: () => true,
                isTextInputFocused: SettingsLogPageHostWiring.IsTextInputFocused,
                focusSearch: () => { },
                navigateNext: () => { },
                navigatePrevious: () => { },
                exportVisibleLog: () => { },
                scrollWindowTop: () => { },
                scrollWindowBottom: () => { },
                scrollHistoryStart: () => { },
                scrollHistoryEnd: () => { },
                closeTransientLayer: () => transientLayerClosed++);

            var keyEvent = ProcessKeyInput(comboBox, Key.Escape);

            windowPreviewObserved.Should().BeTrue();
            windowPreviewSource.Should().BeSameAs(comboBox);
            windowPreviewOriginalSource.Should().BeSameAs(comboBox);
            comboBox.IsDropDownOpen.Should().BeFalse();
            transientLayerClosed.Should().Be(0);
            keyEvent.Handled.Should().BeTrue();
        });
    }

    [Fact]
    public void Non_combobox_escape_still_closes_transient_layer_at_window_route()
    {
        WpfStaTestHost.Run(() =>
        {
            var button = new Button { Content = "日志" };
            var window = new Window
            {
                Width = 320,
                Height = 80,
                Content = button,
            };
            var transientLayerClosed = 0;

            WpfStaTestHost.ShowAndWait(window);
            button.Focus().Should().BeTrue();
            Keyboard.FocusedElement.Should().BeSameAs(button);

            SettingsLogPageHostWiring.WireKeyboardCommands(
                window,
                isLogPageActive: () => true,
                isTextInputFocused: SettingsLogPageHostWiring.IsTextInputFocused,
                focusSearch: () => { },
                navigateNext: () => { },
                navigatePrevious: () => { },
                exportVisibleLog: () => { },
                scrollWindowTop: () => { },
                scrollWindowBottom: () => { },
                scrollHistoryStart: () => { },
                scrollHistoryEnd: () => { },
                closeTransientLayer: () => transientLayerClosed++);

            var keyEvent = ProcessKeyInput(button, Key.Escape);

            transientLayerClosed.Should().Be(1);
            keyEvent.Handled.Should().BeTrue();
        });
    }

    [Fact]
    public void Collapsed_combobox_escape_still_closes_transient_layer_at_window_route()
    {
        WpfStaTestHost.Run(() =>
        {
            var comboBox = new ComboBox
            {
                ItemsSource = new[] { "全部会话", "当前会话" },
            };
            var window = new Window
            {
                Width = 320,
                Height = 80,
                Content = comboBox,
            };
            var transientLayerClosed = 0;

            WpfStaTestHost.ShowAndWait(window);
            comboBox.Focus().Should().BeTrue();
            Keyboard.FocusedElement.Should().BeSameAs(comboBox);
            comboBox.IsDropDownOpen.Should().BeFalse();

            SettingsLogPageHostWiring.WireKeyboardCommands(
                window,
                isLogPageActive: () => true,
                isTextInputFocused: SettingsLogPageHostWiring.IsTextInputFocused,
                focusSearch: () => { },
                navigateNext: () => { },
                navigatePrevious: () => { },
                exportVisibleLog: () => { },
                scrollWindowTop: () => { },
                scrollWindowBottom: () => { },
                scrollHistoryStart: () => { },
                scrollHistoryEnd: () => { },
                closeTransientLayer: () => transientLayerClosed++);

            var keyEvent = ProcessKeyInput(comboBox, Key.Escape);

            transientLayerClosed.Should().Be(1);
            keyEvent.Handled.Should().BeTrue();
        });
    }

    private static KeyEventArgs ProcessKeyInput(UIElement target, Key key)
    {
        var keyEvent = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(target)!,
            0,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
            Source = target,
        };
        target.RaiseEvent(keyEvent);

        if (!keyEvent.Handled)
        {
            var bubbleEvent = new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(target)!,
                0,
                key)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
                Source = target,
            };
            target.RaiseEvent(bubbleEvent);
            keyEvent = bubbleEvent;
        }

        return keyEvent;
    }

    private static void Resolve(Key key, ModifierKeys modifiers, RoutedCommand expected)
    {
        LogWorkbenchCommands.TryResolve(
                key,
                modifiers,
                isTextInputFocused: false,
                out var command)
            .Should().BeTrue();

        command.Should().BeSameAs(expected);
    }
}
