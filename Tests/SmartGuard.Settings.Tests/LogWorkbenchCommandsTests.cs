using System.Windows;
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
