using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawIcon = System.Drawing.Icon;

namespace SmartGuard.Configuration;

public static class BrandIconLoader
{
  private const string EmbeddedResourceName = "SmartGuard.Configuration.Assets.Branding.SmartGuard.ico";
  private const int WinFormsTaskbarIconFrameSize = 32;
  private const int WpfWindowIconFrameSize = 64;

  private static DrawIcon? _cachedWinFormsIcon;
  private static ImageSource? _cachedWpfImageSource;

  internal static int WinFormsLoadCountForTests { get; private set; }

  internal static void ResetCacheForTests()
  {
    _cachedWinFormsIcon?.Dispose();
    _cachedWinFormsIcon = null;
    _cachedWpfImageSource = null;
    WinFormsLoadCountForTests = 0;
  }

  public static DrawIcon LoadWinFormsIcon()
  {
    if (_cachedWinFormsIcon is not null)
      return (DrawIcon)_cachedWinFormsIcon.Clone();

    WinFormsLoadCountForTests++;
    var icon = LoadEmbeddedWinFormsIcon()
      ?? throw new InvalidOperationException("Embedded SmartGuard brand icon is missing.");

    _cachedWinFormsIcon?.Dispose();
    _cachedWinFormsIcon = icon;
    return (DrawIcon)icon.Clone();
  }

  public static ImageSource? LoadWpfImageSource()
  {
    if (_cachedWpfImageSource is not null)
      return _cachedWpfImageSource;

    using var stream = OpenEmbeddedIconStream();
    var loaded = stream is null ? null : TryLoadBestWpfFrame(stream);
    if (loaded is not null)
      _cachedWpfImageSource = loaded;

    return loaded;
  }

  private static DrawIcon? LoadEmbeddedWinFormsIcon()
  {
    try
    {
      using var stream = OpenEmbeddedIconStream();
      if (stream is null)
        return null;

      using var sourceIcon = new DrawIcon(stream);
      return (DrawIcon)sourceIcon.Clone();
    }
    catch
    {
      return null;
    }
  }

  private static Stream? OpenEmbeddedIconStream()
    => typeof(BrandIconLoader).Assembly.GetManifestResourceStream(EmbeddedResourceName);

  private static ImageSource? TryLoadBestWpfFrame(Stream stream)
  {
    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    var frame = decoder.Frames
      .Where(candidate => candidate.PixelWidth > 0 && candidate.PixelHeight > 0)
      .OrderBy(candidate => candidate.PixelWidth < WpfWindowIconFrameSize || candidate.PixelHeight < WpfWindowIconFrameSize ? 1 : 0)
      .ThenBy(candidate => Math.Abs(candidate.PixelWidth - WpfWindowIconFrameSize) + Math.Abs(candidate.PixelHeight - WpfWindowIconFrameSize))
      .ThenBy(candidate => candidate.PixelWidth * candidate.PixelHeight)
      .FirstOrDefault();
    if (frame is null)
      return null;

    if (frame.CanFreeze && !frame.IsFrozen)
      frame.Freeze();

    return frame;
  }
}
