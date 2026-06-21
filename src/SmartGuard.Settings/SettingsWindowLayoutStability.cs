using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

public static class SettingsWindowLayoutStability
{
    public static void Attach(
        Window window,
        Func<bool> isDarkTheme,
        Action stabilizeLayout,
        Action queueLayoutStabilization)
    {
        window.StateChanged += (_, _) =>
        {
            HandleWindowStateChanged(window, isDarkTheme(), window.WindowState);

            if (window.WindowState == WindowState.Minimized)
                return;

            stabilizeLayout();
        };

        window.SizeChanged += (_, _) => queueLayoutStabilization();
    }

    public static void HandleWindowStateChanged(Window window, bool isDarkTheme, WindowState state)
    {
        if (state == WindowState.Minimized)
            return;

        WindowTitleBarTheme.Apply(window, isDarkTheme);
    }

    public static void StabilizeContentLayout(Window window)
    {
        if (window.Content is not FrameworkElement root)
            return;

        root.InvalidateMeasure();
        root.InvalidateArrange();
        root.UpdateLayout();

        if (window.FindName("logScrollViewer") is ScrollViewer logScrollViewer
            && window.FindName("txtLogView") is RichTextBox logView)
        {
            LogViewRichTextRenderer.SynchronizeViewport(logView, logScrollViewer.ViewportWidth);
            logScrollViewer.InvalidateMeasure();
            logScrollViewer.InvalidateArrange();
            logScrollViewer.UpdateLayout();
            ScrollBarAutoHide.NotifyContentChanged(logScrollViewer);
        }
    }
}
