using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FluentAssertions;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
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
}
