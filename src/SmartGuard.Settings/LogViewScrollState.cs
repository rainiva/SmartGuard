namespace SmartGuard.Settings;

public static class LogViewScrollState
{
    public const double TailThresholdPixels = 48;

    public static bool IsNearTail(double scrollableHeight, double verticalOffset)
    {
        if (scrollableHeight <= 0)
            return true;

        return verticalOffset >= scrollableHeight - TailThresholdPixels;
    }

    public static bool IsAtTail(System.Windows.Controls.ScrollViewer scrollViewer)
    {
        return IsNearTail(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset);
    }
}
