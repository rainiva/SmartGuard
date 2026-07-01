using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SmartGuard.Settings;

internal static class ThemeTransitionAnimator
{
    internal static TimeSpan TransitionDuration
    {
        get => ThemeTransitionProfile.TotalDuration;
        set => ThemeTransitionProfile.TotalDuration = value;
    }

    internal static Storyboard? ActiveStoryboard { get; private set; }

    internal static void ResetForTests()
    {
        ActiveStoryboard?.Stop();
        ActiveStoryboard = null;
        ThemeTransitionProfile.TotalDuration = TimeSpan.FromMilliseconds(760);
    }

    internal static void EnsureMutableBrushes(ResourceDictionary resources)
    {
        foreach (var key in ThemePalette.GetColors(isDark: false).Keys)
        {
            if (!resources.Contains(key))
                continue;

            if (resources[key] is SolidColorBrush { IsFrozen: false })
                continue;

            resources[key] = ToMutableBrush(resources[key]);
        }
    }

    internal static void ApplyImmediate(ResourceDictionary resources, bool isDark)
    {
        ActiveStoryboard?.Stop();
        ActiveStoryboard = null;

        EnsureMutableBrushes(resources);
        ApplyPalette(resources, ThemePalette.GetColors(isDark));
    }

    internal static void AnimateTransition(
        Window window,
        ResourceDictionary resources,
        bool isDark,
        Action? onCompleted = null,
        Action? onMidpoint = null)
    {
        ActiveStoryboard?.Stop();
        EnsureMutableBrushes(resources);

        var targetPalette = ThemePalette.GetColors(isDark);
        var storyboard = new Storyboard
        {
            Duration = ThemeTransitionProfile.TotalDuration,
        };

        var easing = new PowerEase { Power = 2.4, EasingMode = EasingMode.EaseInOut };

        foreach (var layer in ThemeTransitionProfile.Layers)
        {
            var layerDuration = TimeSpan.FromMilliseconds(layer.DurationMilliseconds);
            var layerBegin = TimeSpan.FromMilliseconds(layer.BeginMilliseconds);

            foreach (var key in layer.Keys)
            {
                if (!targetPalette.TryGetValue(key, out var targetColor) || !resources.Contains(key))
                    continue;

                if (resources[key] is not SolidColorBrush brush)
                {
                    resources[key] = new SolidColorBrush(targetColor);
                    continue;
                }

                var animation = new ColorAnimation
                {
                    To = targetColor,
                    Duration = layerDuration,
                    BeginTime = layerBegin,
                    EasingFunction = easing,
                };
                Storyboard.SetTarget(animation, brush);
                Storyboard.SetTargetProperty(animation, new PropertyPath(SolidColorBrush.ColorProperty));
                storyboard.Children.Add(animation);
            }
        }

        AddOverlayPulse(window, isDark, storyboard);

        var midpointTimer = onMidpoint is null
            ? null
            : CreateOneShotTimer(window, ThemeTransitionProfile.TitleBarApplyAt, onMidpoint);

        storyboard.Completed += (_, _) =>
        {
            midpointTimer?.Stop();
            ActiveStoryboard = null;
            HideOverlay(window);
            ApplyPalette(resources, targetPalette);
            onCompleted?.Invoke();
        };

        ActiveStoryboard = storyboard;
        storyboard.Begin();
    }

    private static void AddOverlayPulse(Window window, bool isDark, Storyboard storyboard)
    {
        var overlay = FindOverlay(window);
        if (overlay is null)
            return;

        overlay.Background = new SolidColorBrush(isDark ? Color.FromArgb(0x55, 0x00, 0x00, 0x00) : Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
        overlay.Opacity = 0;

        var opacityAnimation = new DoubleAnimationUsingKeyFrames
        {
            Duration = ThemeTransitionProfile.TotalDuration,
        };
        opacityAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0.16, KeyTime.FromPercent(0.34), new SineEase { EasingMode = EasingMode.EaseOut }));
        opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseInOut }));

        Storyboard.SetTarget(opacityAnimation, overlay);
        Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(opacityAnimation);
    }

    private static void HideOverlay(Window window)
    {
        var overlay = FindOverlay(window);
        if (overlay is null)
            return;

        overlay.Opacity = 0;
        overlay.Background = Brushes.Transparent;
    }

    private static Border? FindOverlay(Window window)
        => window.FindName("themeTransitionOverlay") as Border;

    private static DispatcherTimer CreateOneShotTimer(Window window, TimeSpan delay, Action callback)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Render, window.Dispatcher)
        {
            Interval = delay,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            callback();
        };
        timer.Start();
        return timer;
    }

    private static void ApplyPalette(ResourceDictionary resources, IReadOnlyDictionary<string, Color> palette)
    {
        foreach (var (key, color) in palette)
        {
            if (!resources.Contains(key))
                continue;

            if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
                continue;
            }

            resources[key] = new SolidColorBrush(color);
        }
    }

    private static SolidColorBrush ToMutableBrush(object resource)
    {
        return resource switch
        {
            SolidColorBrush brush => new SolidColorBrush(brush.Color),
            Color color => new SolidColorBrush(color),
            _ => throw new InvalidOperationException($"Unsupported theme resource type: {resource.GetType().Name}"),
        };
    }
}
