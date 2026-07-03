using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class ThemeTransitionAnimatorTests
{
    [Fact]
    public void Light_palette_uses_expected_window_background()
    {
        ThemePalette.GetColors(isDark: false)["WindowBackground"]
            .Should().Be(Color.FromRgb(0xF3, 0xF3, 0xF3));
    }

    [Fact]
    public void Dark_palette_uses_expected_window_background()
    {
        ThemePalette.GetColors(isDark: true)["WindowBackground"]
            .Should().Be(Color.FromRgb(0x20, 0x20, 0x20));
    }

    [Fact]
    public void ApplyImmediate_sets_dark_brush_colors()
    {
        WpfStaTestHost.Run(() =>
        {
            var resources = CreateSampleResources();
            ThemeTransitionAnimator.EnsureMutableBrushes(resources);
            ThemeTransitionAnimator.ApplyImmediate(resources, isDark: true);

            GetBrushColor(resources, "WindowBackground").Should().Be(Color.FromRgb(0x20, 0x20, 0x20));
            GetBrushColor(resources, "TextAccent").Should().Be(Color.FromRgb(0x4C, 0xC2, 0xFF));
        });
    }

    [Fact]
    public void EnsureMutableBrushes_replaces_frozen_brushes()
    {
        WpfStaTestHost.Run(() =>
        {
            var resources = CreateSampleResources();
            ((SolidColorBrush)resources["WindowBackground"]).IsFrozen.Should().BeTrue();

            ThemeTransitionAnimator.EnsureMutableBrushes(resources);

            ((SolidColorBrush)resources["WindowBackground"]).IsFrozen.Should().BeFalse();
        });
    }

    [Fact]
    public void InstallRuntimeThemeLayer_moves_theme_colors_to_mutable_merged_dictionary()
    {
        WpfStaTestHost.Run(() =>
        {
            var window = SettingsXamlLoader.TryLoadEmbeddedWindow(out var error);
            window.Should().NotBeNull(error);

            var runtime = ThemeTransitionAnimator.InstallRuntimeThemeLayer(window!.Resources);
            runtime.Should().NotBeNull();
            window.Resources.Keys.OfType<string>().Should().NotContain("WindowBackground");
            runtime.Contains("WindowBackground").Should().BeTrue();
            ((SolidColorBrush)runtime["WindowBackground"]).IsFrozen.Should().BeFalse();
        });
    }

    [Fact]
    public void AnimateTransition_succeeds_when_theme_brushes_are_frozen()
    {
        WpfStaTestHost.Run(() =>
        {
            ThemeTransitionAnimator.ResetForTests();

            var resources = CreateSampleResources();
            ThemeTransitionAnimator.EnsureMutableBrushes(resources);
            ThemeTransitionAnimator.ApplyImmediate(resources, isDark: false);

            foreach (var key in ThemePalette.GetColors(isDark: false).Keys)
            {
                if (resources[key] is SolidColorBrush brush)
                    brush.Freeze();
            }

            var window = new Window();
            window.Resources.MergedDictionaries.Add(resources);

            var act = () => ThemeTransitionAnimator.AnimateTransition(window, resources, isDark: true);
            act.Should().NotThrow();
            ThemeTransitionAnimator.ActiveStoryboard.Should().NotBeNull();
        });
    }

    [Fact]
    public void AnimateTransition_uses_layered_duration_and_smooth_easing()
    {
        WpfStaTestHost.Run(() =>
        {
            ThemeTransitionAnimator.ResetForTests();
            ThemeTransitionAnimator.TransitionDuration = TimeSpan.FromMilliseconds(760);

            var resources = CreateSampleResources();
            ThemeTransitionAnimator.EnsureMutableBrushes(resources);
            ThemeTransitionAnimator.ApplyImmediate(resources, isDark: false);

            var window = new Window();
            window.Resources.MergedDictionaries.Add(resources);

            ThemeTransitionAnimator.AnimateTransition(window, resources, isDark: true);

            ThemeTransitionAnimator.ActiveStoryboard.Should().NotBeNull();
            ThemeTransitionAnimator.ActiveStoryboard!.Duration.TimeSpan.Should().Be(TimeSpan.FromMilliseconds(760));

            var colorAnimations = ThemeTransitionAnimator.ActiveStoryboard.Children
                .OfType<ColorAnimation>()
                .ToList();
            colorAnimations.Should().NotBeEmpty();
            colorAnimations[0].EasingFunction.Should().BeOfType<PowerEase>();
            colorAnimations[0].EasingFunction!.As<PowerEase>().EasingMode.Should().Be(EasingMode.EaseInOut);

            var accentAnimation = colorAnimations.First(animation => animation.BeginTime == TimeSpan.FromMilliseconds(160));
            accentAnimation.Duration.TimeSpan.Should().Be(TimeSpan.FromMilliseconds(600));
        });
    }

    [Fact]
    public void AnimateTransition_does_not_invoke_midpoint_callback_synchronously()
    {
        WpfStaTestHost.Run(() =>
        {
            ThemeTransitionAnimator.ResetForTests();
            ThemeTransitionAnimator.TransitionDuration = TimeSpan.FromMilliseconds(760);

            var resources = CreateSampleResources();
            ThemeTransitionAnimator.EnsureMutableBrushes(resources);
            ThemeTransitionAnimator.ApplyImmediate(resources, isDark: false);

            var window = new Window();
            window.Resources.MergedDictionaries.Add(resources);

            var midpointCalls = 0;
            ThemeTransitionAnimator.AnimateTransition(
                window,
                resources,
                isDark: true,
                onMidpoint: () => midpointCalls++);

            midpointCalls.Should().Be(0,
                "theme animation midpoint work must be deferred so the title bar does not snap before the content transition starts");
        });
    }

    [Fact]
    public void AnimateTransition_succeeds_on_real_settings_window_resources()
    {
        WpfStaTestHost.Run(() =>
        {
            SettingsUiTestMode.SetEnabled(false);
            ThemeTransitionAnimator.ResetForTests();

            var installRoot = Path.Combine(Path.GetTempPath(), "sg-theme-animator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);
            Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
            File.WriteAllText(
                Path.Combine(installRoot, "SmartGuard.config.json"),
                "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000,\"ThemeFollowSystem\":false,\"ThemeIsDark\":false}");

            try
            {
                var repository = new GuardConfigRepository(Path.Combine(installRoot, "SmartGuard.config.json"));
                var config = repository.LoadOrDefault(installRoot);
                var controller = SettingsWindowController.TryCreate(installRoot, repository, config);
                controller.Should().NotBeNull();

                var windowField = typeof(SettingsWindowController).GetField(
                    "_window",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var window = (Window)windowField!.GetValue(controller!)!;
                WpfStaTestHost.ShowAndWait(window);

                var runtimeResources = window.Resources.MergedDictionaries[0];
                var initialBackground = GetBrushColor(runtimeResources, "WindowBackground");
                var act = () => ThemeTransitionAnimator.AnimateTransition(window, runtimeResources, isDark: true);

                act.Should().NotThrow();
                GetBrushColor(runtimeResources, "WindowBackground").Should().Be(
                    initialBackground,
                    "real Settings resources should start an animated transition instead of snapping to the final palette immediately");

                WaitForDispatcher(window, ThemeTransitionAnimator.TransitionDuration + TimeSpan.FromMilliseconds(180));

                GetBrushColor(runtimeResources, "WindowBackground").Should().Be(
                    ThemePalette.GetColors(isDark: true)["WindowBackground"],
                    "the fallback path must still converge to the dark palette on the real Settings window");
            }
            finally
            {
                SettingsUiTestMode.SetEnabled(true);
                try { Directory.Delete(installRoot, true); } catch { }
            }
        });
    }

    private static ResourceDictionary CreateSampleResources()
    {
        var resources = new ResourceDictionary();
        foreach (var (key, color) in ThemePalette.GetColors(isDark: false))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        return resources;
    }

    private static Color GetBrushColor(ResourceDictionary resources, string key)
        => ((SolidColorBrush)resources[key]).Color;

    private static void WaitForDispatcher(Window window, TimeSpan delay)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Render, window.Dispatcher)
        {
            Interval = delay,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
