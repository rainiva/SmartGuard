using System.Windows.Media;

namespace SmartGuard.Settings;

internal static class ThemePalette
{
    internal static IReadOnlyDictionary<string, Color> GetColors(bool isDark)
        => isDark ? Dark : Light;

    internal static readonly IReadOnlyDictionary<string, Color> Light = new Dictionary<string, Color>
    {
        ["WindowBackground"] = ColorFrom("#F3F3F3"),
        ["WindowForeground"] = ColorFrom("#1A1A1A"),
        ["NavigationBackground"] = ColorFrom("#F9F9F9"),
        ["NavigationItemForeground"] = ColorFrom("#1A1A1A"),
        ["NavigationItemSelectedBackground"] = ColorFrom("#E5E5E5"),
        ["NavigationItemSelectedForeground"] = ColorFrom("#005FB8"),
        ["NavigationItemHoverBackground"] = ColorFrom("#EEEEEE"),
        ["NavigationBorderBrush"] = ColorFrom("#E0E0E0"),
        ["CardBackground"] = ColorFrom("#FFFFFF"),
        ["CardBorderBrush"] = ColorFrom("#E5E5E5"),
        ["CardShadowColor"] = ColorFrom("#20000000"),
        ["TextPrimary"] = ColorFrom("#1A1A1A"),
        ["TextSecondary"] = ColorFrom("#616161"),
        ["TextTertiary"] = ColorFrom("#8A8A8A"),
        ["TextAccent"] = ColorFrom("#005FB8"),
        ["PrimaryButtonBackground"] = ColorFrom("#005FB8"),
        ["PrimaryButtonForeground"] = ColorFrom("#FFFFFF"),
        ["PrimaryButtonHoverBackground"] = ColorFrom("#004578"),
        ["SecondaryButtonBackground"] = ColorFrom("#FFFFFF"),
        ["SecondaryButtonForeground"] = ColorFrom("#1A1A1A"),
        ["SecondaryButtonBorderBrush"] = ColorFrom("#D1D1D1"),
        ["SecondaryButtonHoverBackground"] = ColorFrom("#F0F0F0"),
        ["ToggleTrackOff"] = ColorFrom("#C4C4C4"),
        ["ToggleTrackOn"] = ColorFrom("#005FB8"),
        ["ToggleThumb"] = ColorFrom("#FFFFFF"),
        ["NumberBoxBackground"] = ColorFrom("#FFFFFF"),
        ["NumberBoxBorderBrush"] = ColorFrom("#D1D1D1"),
        ["NumberBoxButtonBackground"] = ColorFrom("#F0F0F0"),
        ["NumberBoxButtonHoverBackground"] = ColorFrom("#E5E5E5"),
        ["InfoBarBackground"] = ColorFrom("#E8F3FF"),
        ["InfoBarForeground"] = ColorFrom("#005FB8"),
        ["InfoBarBorderBrush"] = ColorFrom("#B3D7F7"),
        ["DividerBrush"] = ColorFrom("#E5E5E5"),
    };

    internal static readonly IReadOnlyDictionary<string, Color> Dark = new Dictionary<string, Color>
    {
        ["WindowBackground"] = ColorFrom("#202020"),
        ["WindowForeground"] = ColorFrom("#FFFFFF"),
        ["NavigationBackground"] = ColorFrom("#1C1C1C"),
        ["NavigationItemForeground"] = ColorFrom("#FFFFFF"),
        ["NavigationItemSelectedBackground"] = ColorFrom("#2D2D2D"),
        ["NavigationItemSelectedForeground"] = ColorFrom("#4CC2FF"),
        ["NavigationItemHoverBackground"] = ColorFrom("#2A2A2A"),
        ["NavigationBorderBrush"] = ColorFrom("#3A3A3A"),
        ["CardBackground"] = ColorFrom("#2D2D2D"),
        ["CardBorderBrush"] = ColorFrom("#3A3A3A"),
        ["CardShadowColor"] = ColorFrom("#40000000"),
        ["TextPrimary"] = ColorFrom("#FFFFFF"),
        ["TextSecondary"] = ColorFrom("#B0B0B0"),
        ["TextTertiary"] = ColorFrom("#8A8A8A"),
        ["TextAccent"] = ColorFrom("#4CC2FF"),
        ["PrimaryButtonBackground"] = ColorFrom("#4CC2FF"),
        ["PrimaryButtonForeground"] = ColorFrom("#1A1A1A"),
        ["PrimaryButtonHoverBackground"] = ColorFrom("#3AA8E0"),
        ["SecondaryButtonBackground"] = ColorFrom("#2D2D2D"),
        ["SecondaryButtonForeground"] = ColorFrom("#FFFFFF"),
        ["SecondaryButtonBorderBrush"] = ColorFrom("#5A5A5A"),
        ["SecondaryButtonHoverBackground"] = ColorFrom("#3A3A3A"),
        ["ToggleTrackOff"] = ColorFrom("#5A5A5A"),
        ["ToggleTrackOn"] = ColorFrom("#4CC2FF"),
        ["ToggleThumb"] = ColorFrom("#FFFFFF"),
        ["NumberBoxBackground"] = ColorFrom("#2D2D2D"),
        ["NumberBoxBorderBrush"] = ColorFrom("#5A5A5A"),
        ["NumberBoxButtonBackground"] = ColorFrom("#3A3A3A"),
        ["NumberBoxButtonHoverBackground"] = ColorFrom("#4A4A4A"),
        ["InfoBarBackground"] = ColorFrom("#1A3A5C"),
        ["InfoBarForeground"] = ColorFrom("#4CC2FF"),
        ["InfoBarBorderBrush"] = ColorFrom("#2A5A8A"),
        ["DividerBrush"] = ColorFrom("#3A3A3A"),
    };

    private static Color ColorFrom(string hex)
        => (Color)ColorConverter.ConvertFromString(hex)!;
}
