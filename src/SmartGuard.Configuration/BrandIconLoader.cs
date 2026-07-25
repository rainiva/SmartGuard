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
  private const string EmbeddedToastResourceName = "SmartGuard.Configuration.Assets.Branding.SmartGuard-toast.png";
  private const int WinFormsTaskbarIconFrameSize = 32;
  private const int WpfWindowIconFrameSize = 64;

  private static readonly object WinFormsIconSync = new();
  private static DrawIcon? _cachedWinFormsIcon;
  private static ImageSource? _cachedWpfImageSource;

  internal static int WinFormsLoadCountForTests { get; private set; }

  internal static void ResetCacheForTests()
  {
    lock (WinFormsIconSync)
    {
      _cachedWinFormsIcon?.Dispose();
      _cachedWinFormsIcon = null;
      WinFormsLoadCountForTests = 0;
    }

    _cachedWpfImageSource = null;
  }

  public static DrawIcon LoadWinFormsIcon()
  {
    lock (WinFormsIconSync)
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

  public static void SaveEmbeddedIconToFile(string path)
  {
    SaveEmbeddedFileToFile(path, OpenEmbeddedIconStream(), "Embedded SmartGuard brand icon is missing.");
  }

  public static void SaveEmbeddedToastIconToFile(string path)
  {
    SaveEmbeddedFileToFile(path, OpenEmbeddedToastIconStream(), "Embedded SmartGuard toast icon is missing.");
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

  private static Stream? OpenEmbeddedToastIconStream()
    => typeof(BrandIconLoader).Assembly.GetManifestResourceStream(EmbeddedToastResourceName);

  private static void SaveEmbeddedFileToFile(string path, Stream? stream, string missingMessage)
  {
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);

    using var sourceStream = stream
      ?? throw new InvalidOperationException(missingMessage);
    using var fileStream = File.Create(path);
    sourceStream.CopyTo(fileStream);
  }

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
