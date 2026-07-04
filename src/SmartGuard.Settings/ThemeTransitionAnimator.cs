using System.Diagnostics;
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
    private static DispatcherTimer? ActiveTimerTransition { get; set; }

    internal static void ResetForTests()
    {
        ActiveStoryboard?.Stop();
        ActiveStoryboard = null;
        ActiveTimerTransition?.Stop();
        ActiveTimerTransition = null;
        ThemeTransitionProfile.TotalDuration = TimeSpan.FromMilliseconds(760);
    }

    internal static ResourceDictionary InstallRuntimeThemeLayer(ResourceDictionary resources)
    {
        var runtime = new ResourceDictionary();
        foreach (var key in ThemePalette.GetColors(isDark: false).Keys)
        {
            if (!resources.Contains(key))
                continue;

            runtime[key] = ToMutableBrush(resources[key]);
            resources.Remove(key);
        }

        resources.MergedDictionaries.Insert(0, runtime);
        return runtime;
    }

    internal static void EnsureMutableBrushes(ResourceDictionary resources)
    {
        foreach (var key in ThemePalette.GetColors(isDark: false).Keys)
        {
            if (!resources.Contains(key))
                continue;

            resources[key] = ToMutableBrush(resources[key]);
        }
    }

    internal static void ApplyImmediate(ResourceDictionary resources, bool isDark)
    {
        ActiveStoryboard?.Stop();
        ActiveStoryboard = null;
        ActiveTimerTransition?.Stop();
        ActiveTimerTransition = null;

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

                var brush = ResolveAnimatableBrush(resources, key, targetColor);

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
        ActiveTimerTransition?.Stop();
        ActiveTimerTransition = null;

        try
        {
            storyboard.Begin();
        }
        catch (InvalidOperationException)
        {
            ActiveStoryboard = null;
            midpointTimer?.Stop();
            StartTimerDrivenTransition(window, resources, targetPalette, easing, onCompleted, onMidpoint);
        }
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

    private static void StartTimerDrivenTransition(
        Window window,
        ResourceDictionary resources,
        IReadOnlyDictionary<string, Color> targetPalette,
        IEasingFunction easing,
        Action? onCompleted,
        Action? onMidpoint)
    {
        var startPalette = targetPalette.Keys
            .Where(resources.Contains)
            .ToDictionary(key => key, key => GetBrushColor(resources, key, targetPalette[key]));

        var overlay = FindOverlay(window);
        if (overlay is not null)
        {
            overlay.Background = new SolidColorBrush(targetPalette["WindowBackground"].R < 0x60
                ? Color.FromArgb(0x55, 0x00, 0x00, 0x00)
                : Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
            overlay.Opacity = 0;
        }

        var stopwatch = Stopwatch.StartNew();
        var midpointApplied = false;
        var timer = new DispatcherTimer(DispatcherPriority.Render, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };

        timer.Tick += (_, _) =>
        {
            var elapsed = stopwatch.Elapsed;
            if (!midpointApplied && elapsed >= ThemeTransitionProfile.TitleBarApplyAt)
            {
                midpointApplied = true;
                onMidpoint?.Invoke();
            }

            foreach (var layer in ThemeTransitionProfile.Layers)
            {
                var layerBegin = TimeSpan.FromMilliseconds(layer.BeginMilliseconds);
                var layerDuration = TimeSpan.FromMilliseconds(layer.DurationMilliseconds);
                var layerProgress = Math.Clamp(
                    (elapsed - layerBegin).TotalMilliseconds / Math.Max(1, layerDuration.TotalMilliseconds),
                    0,
                    1);
                var easedProgress = easing.Ease(layerProgress);

                foreach (var key in layer.Keys)
                {
                    if (!targetPalette.TryGetValue(key, out var targetColor) || !startPalette.TryGetValue(key, out var startColor))
                        continue;

                    ApplyInterpolatedColor(resources, key, InterpolateColor(startColor, targetColor, easedProgress));
                }
            }

            if (overlay is not null)
                overlay.Opacity = ComputeOverlayOpacity(elapsed);

            if (elapsed < ThemeTransitionProfile.TotalDuration)
                return;

            timer.Stop();
            ActiveTimerTransition = null;

            if (!midpointApplied)
                onMidpoint?.Invoke();

            HideOverlay(window);
            ApplyPalette(resources, targetPalette);
            onCompleted?.Invoke();
        };

        ActiveTimerTransition = timer;
        timer.Start();
    }

    private static double ComputeOverlayOpacity(TimeSpan elapsed)
    {
        var total = Math.Max(1, ThemeTransitionProfile.TotalDuration.TotalMilliseconds);
        var progress = Math.Clamp(elapsed.TotalMilliseconds / total, 0, 1);
        if (progress <= 0.34)
            return 0.16 * (progress / 0.34);

        return 0.16 * (1 - ((progress - 0.34) / 0.66));
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

    private static void ApplyInterpolatedColor(ResourceDictionary resources, string key, Color color)
    {
        if (!resources.Contains(key))
            return;

        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static SolidColorBrush ResolveAnimatableBrush(
        ResourceDictionary resources,
        string key,
        Color targetColor)
    {
        var currentColor = resources[key] switch
        {
            SolidColorBrush brush => brush.Color,
            Color color => color,
            _ => targetColor,
        };

        // Real Settings resources can end up sealed/frozen by the time the runtime
        // animation begins. Swap in a fresh mutable brush for each animated key so
        // the storyboard always targets an animatable instance.
        var mutableBrush = new SolidColorBrush(currentColor);
        resources[key] = mutableBrush;

        return mutableBrush;
    }

    private static Color GetBrushColor(ResourceDictionary resources, string key, Color fallback)
    {
        return resources[key] switch
        {
            SolidColorBrush brush => brush.Color,
            Color color => color,
            _ => fallback,
        };
    }

    private static Color InterpolateColor(Color from, Color to, double progress)
    {
        byte Lerp(byte start, byte end)
            => (byte)Math.Round(start + ((end - start) * progress));

        return Color.FromArgb(
            Lerp(from.A, to.A),
            Lerp(from.R, to.R),
            Lerp(from.G, to.G),
            Lerp(from.B, to.B));
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
