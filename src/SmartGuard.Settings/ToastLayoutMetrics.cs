namespace SmartGuard.Settings;

public static class ToastLayoutMetrics
{
    public const double ContentPageRightMargin = 24;

    public const double InlinePaddingVertical = 8;
    public const double InlinePaddingHorizontal = 12;
    public const double InlineIconSize = 22;
    public const double InlineFontSize = 13;
    public const double InlineIconFontSize = 12;
    public const double InlineCornerRadius = 8;
    public const double InlineOuterMargin = 6;

    public static double ToastContainerRightMargin => ContentPageRightMargin - InlineOuterMargin;

    public const double FloatingPaddingVertical = 8;
    public const double FloatingPaddingHorizontal = 12;
    public const double FloatingFontSize = 12;
    public const double FloatingIconFontSize = 14;
    public const double FloatingCornerRadius = 10;
}
