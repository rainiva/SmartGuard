using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace SmartGuard.Settings;

public static class ScrollBarAutoHide
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ScrollBarAutoHide),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty HideTimerProperty = DependencyProperty.RegisterAttached(
        "HideTimer",
        typeof(DispatcherTimer),
        typeof(ScrollBarAutoHide));

    internal static TimeSpan HideDelay { get; set; } = TimeSpan.FromMilliseconds(800);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static void Attach(ScrollViewer scrollViewer)
    {
        scrollViewer.Loaded -= OnLoaded;
        scrollViewer.Loaded += OnLoaded;
        scrollViewer.ScrollChanged -= OnScrollChanged;
        scrollViewer.ScrollChanged += OnScrollChanged;
        scrollViewer.MouseEnter -= OnMouseEnter;
        scrollViewer.MouseEnter += OnMouseEnter;
        scrollViewer.MouseLeave -= OnMouseLeave;
        scrollViewer.MouseLeave += OnMouseLeave;

        if (scrollViewer.IsLoaded)
            InitializeScrollBars(scrollViewer);
    }

    public static void NotifyScrollActivity(ScrollViewer scrollViewer)
    {
        ShowScrollBars(scrollViewer);
        RestartHideTimer(scrollViewer);
    }

    public static void NotifyContentChanged(ScrollViewer scrollViewer)
    {
        scrollViewer.UpdateLayout();
        RefreshHorizontalScrollBar(scrollViewer);
    }

    public static bool IsScrollBarVisible(ScrollViewer scrollViewer, bool vertical)
    {
        var scrollBar = FindScrollBar(scrollViewer, vertical);
        return scrollBar is not null && scrollBar.Opacity > 0.01;
    }

    internal static void HideNowForTesting(ScrollViewer scrollViewer)
    {
        HideScrollBars(scrollViewer);
        StopHideTimer(scrollViewer);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer && (bool)e.NewValue)
            Attach(scrollViewer);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            InitializeScrollBars(scrollViewer);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        if (e.VerticalChange == 0 && e.HorizontalChange == 0)
            return;

        NotifyScrollActivity(scrollViewer);
    }

    private static void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            ShowScrollBars(scrollViewer);
            StopHideTimer(scrollViewer);
        }
    }

    private static void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
            RestartHideTimer(scrollViewer);
    }

    private static void InitializeScrollBars(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        HideScrollBars(scrollViewer);
        RefreshHorizontalScrollBar(scrollViewer);
    }

    private static void ShowScrollBars(ScrollViewer scrollViewer)
    {
        SetScrollBarOpacity(scrollViewer, vertical: true, opacity: 1);
        SetScrollBarOpacity(scrollViewer, vertical: false, opacity: 1);
    }

    private static void HideScrollBars(ScrollViewer scrollViewer)
    {
        SetScrollBarOpacity(scrollViewer, vertical: true, opacity: 0);
        if (scrollViewer.ScrollableWidth <= 0.5)
            SetScrollBarOpacity(scrollViewer, vertical: false, opacity: 0);
    }

    private static void SetScrollBarOpacity(ScrollViewer scrollViewer, bool vertical, double opacity)
    {
        var scrollBar = FindScrollBar(scrollViewer, vertical);
        if (scrollBar is null)
            return;

        scrollBar.Opacity = opacity;
        scrollBar.IsHitTestVisible = opacity > 0.01;
    }

    private static ScrollBar? FindScrollBar(ScrollViewer scrollViewer, bool vertical)
    {
        scrollViewer.ApplyTemplate();
        var partName = vertical ? "PART_VerticalScrollBar" : "PART_HorizontalScrollBar";
        return scrollViewer.Template?.FindName(partName, scrollViewer) as ScrollBar;
    }

    private static void RestartHideTimer(ScrollViewer scrollViewer)
    {
        var timer = GetHideTimer(scrollViewer);
        if (timer is null)
        {
            timer = new DispatcherTimer(HideDelay, DispatcherPriority.Background, (_, _) =>
            {
                HideScrollBars(scrollViewer);
                StopHideTimer(scrollViewer);
            }, scrollViewer.Dispatcher);
            SetHideTimer(scrollViewer, timer);
        }
        else
        {
            timer.Interval = HideDelay;
        }

        timer.Stop();
        timer.Start();
    }

    private static void StopHideTimer(ScrollViewer scrollViewer)
    {
        var timer = GetHideTimer(scrollViewer);
        timer?.Stop();
    }

    private static DispatcherTimer? GetHideTimer(ScrollViewer scrollViewer)
        => scrollViewer.GetValue(HideTimerProperty) as DispatcherTimer;

    private static void SetHideTimer(ScrollViewer scrollViewer, DispatcherTimer timer)
        => scrollViewer.SetValue(HideTimerProperty, timer);

    private static void RefreshHorizontalScrollBar(ScrollViewer scrollViewer)
    {
        scrollViewer.UpdateLayout();
        var opacity = scrollViewer.ScrollableWidth > 0.5 ? 1.0 : 0.0;
        SetScrollBarOpacity(scrollViewer, vertical: false, opacity: opacity);
    }
}
