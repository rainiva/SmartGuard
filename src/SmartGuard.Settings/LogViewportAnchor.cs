namespace SmartGuard.Settings;

public sealed record LogViewportAnchor(
  string EventId,
  double ItemPixelOffset,
  double VerticalOffset,
  int ItemIndex);
