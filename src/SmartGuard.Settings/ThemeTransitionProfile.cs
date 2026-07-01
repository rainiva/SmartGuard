namespace SmartGuard.Settings;

internal static class ThemeTransitionProfile
{
    internal static TimeSpan TotalDuration { get; set; } = TimeSpan.FromMilliseconds(760);

    internal static readonly ThemeLayer[] Layers =
    [
        new ThemeLayer(
            [
                "WindowBackground",
                "WindowForeground",
                "NavigationBackground",
                "NavigationBorderBrush",
                "NavigationItemHoverBackground",
                "CardBackground",
                "CardBorderBrush",
                "CardShadowColor",
                "DividerBrush",
            ],
            0,
            720),
        new ThemeLayer(
            [
                "NavigationItemForeground",
                "NavigationItemSelectedBackground",
                "NavigationItemSelectedForeground",
                "TextPrimary",
                "TextSecondary",
                "TextTertiary",
                "NumberBoxBackground",
                "NumberBoxBorderBrush",
                "NumberBoxButtonBackground",
                "NumberBoxButtonHoverBackground",
                "SecondaryButtonBackground",
                "SecondaryButtonForeground",
                "SecondaryButtonBorderBrush",
                "SecondaryButtonHoverBackground",
                "InfoBarBackground",
                "InfoBarForeground",
                "InfoBarBorderBrush",
            ],
            80,
            680),
        new ThemeLayer(
            [
                "TextAccent",
                "PrimaryButtonBackground",
                "PrimaryButtonForeground",
                "PrimaryButtonHoverBackground",
                "ToggleTrackOff",
                "ToggleTrackOn",
                "ToggleThumb",
            ],
            160,
            600),
    ];

    internal static TimeSpan TitleBarApplyAt =>
        TimeSpan.FromMilliseconds(TotalDuration.TotalMilliseconds * 0.42);

    internal sealed record ThemeLayer(string[] Keys, int BeginMilliseconds, int DurationMilliseconds);
}
