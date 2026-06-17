namespace SmartGuard.LogViewer;

public static class LogViewerTagPalette
{
  public static Color BodyColor { get; } = Color.FromArgb(30, 30, 30);

  public static Color GetTagColor(string tag)
  {
    return tag.ToUpperInvariant() switch
    {
      "INFO" => Color.FromArgb(26, 111, 191),
      "WARN" => Color.FromArgb(196, 122, 0),
      "ERROR" => Color.FromArgb(198, 40, 40),
      "HEART" => Color.FromArgb(95, 107, 122),
      _ => BodyColor,
    };
  }
}
