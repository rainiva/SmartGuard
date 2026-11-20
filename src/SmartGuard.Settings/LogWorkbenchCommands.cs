using System.Windows.Input;

namespace SmartGuard.Settings;

internal static class LogWorkbenchCommands
{
    internal static RoutedUICommand FocusSearchCommand { get; } = Create("聚焦日志查询");
    internal static RoutedUICommand NavigateNextCommand { get; } = Create("下一个日志匹配");
    internal static RoutedUICommand NavigatePreviousCommand { get; } = Create("上一个日志匹配");
    internal static RoutedUICommand ExportCommand { get; } = Create("导出可见日志");
    internal static RoutedUICommand ScrollWindowTopCommand { get; } = Create("跳到当前窗口顶部");
    internal static RoutedUICommand ScrollWindowBottomCommand { get; } = Create("跳到当前窗口底部");
    internal static RoutedUICommand ScrollHistoryStartCommand { get; } = Create("跳到历史首部");
    internal static RoutedUICommand ScrollHistoryEndCommand { get; } = Create("跳到历史尾部");
    internal static RoutedUICommand CloseTransientLayerCommand { get; } = Create("关闭临时层");

    internal static bool TryResolve(
        Key key,
        ModifierKeys modifiers,
        bool isTextInputFocused,
        out RoutedCommand command)
    {
        command = null!;

        if (modifiers == ModifierKeys.Control)
        {
            command = key switch
            {
                Key.F or Key.L => FocusSearchCommand,
                Key.E => ExportCommand,
                Key.Home when !isTextInputFocused => ScrollHistoryStartCommand,
                Key.End when !isTextInputFocused => ScrollHistoryEndCommand,
                _ => null!,
            };

            return command is not null;
        }

        if (modifiers == ModifierKeys.None)
        {
            command = key switch
            {
                Key.F3 or Key.Enter => NavigateNextCommand,
                Key.Home when !isTextInputFocused => ScrollWindowTopCommand,
                Key.End when !isTextInputFocused => ScrollWindowBottomCommand,
                Key.Escape => CloseTransientLayerCommand,
                _ => null!,
            };

            return command is not null;
        }

        if (modifiers == ModifierKeys.Shift && key is Key.F3 or Key.Enter)
        {
            command = NavigatePreviousCommand;
            return true;
        }

        return false;
    }

    private static RoutedUICommand Create(string text)
        => new(text, text, typeof(LogWorkbenchCommands));
}
