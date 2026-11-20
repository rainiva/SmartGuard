namespace SmartGuard.Settings;

public static class LogViewScrollState
{
    public const double TailThresholdPixels = 48;
    public const double HeadThresholdPixels = 48;

    public static bool IsNearTail(double scrollableHeight, double verticalOffset)
    {
        if (scrollableHeight <= 0)
            return true;

        return verticalOffset >= scrollableHeight - TailThresholdPixels;
    }

    public static bool IsNearHead(double scrollableHeight, double verticalOffset)
    {
        if (scrollableHeight <= 0)
            return verticalOffset <= HeadThresholdPixels;

        return verticalOffset <= HeadThresholdPixels;
    }

    public static bool IsAtTail(System.Windows.Controls.ScrollViewer scrollViewer)
        => IsNearTail(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset);

    public static bool IsNearTop(System.Windows.Controls.ScrollViewer scrollViewer)
        => IsNearHead(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset);
}
