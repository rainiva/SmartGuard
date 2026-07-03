using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawIcon = System.Drawing.Icon;

namespace SmartGuard.Configuration;

public static class BrandIconLoader
{
  private const string EmbeddedSettingsIconUri = "pack://application:,,,/Assets/SmartGuard.ico";
  private const int WinFormsTaskbarIconFrameSize = 32;
  private const int WpfWindowIconFrameSize = 64;

  private static string? _cachedIconPath;
  private static DrawIcon? _cachedWinFormsIcon;
  private static ImageSource? _cachedWpfImageSource;
  private static string? _cachedWpfInstallRoot;

  internal static int WinFormsLoadCountForTests { get; private set; }

  internal static void ResetCacheForTests()
  {
    _cachedWinFormsIcon?.Dispose();
    _cachedWinFormsIcon = null;
    _cachedIconPath = null;
    _cachedWpfImageSource = null;
    _cachedWpfInstallRoot = null;
    WinFormsLoadCountForTests = 0;
  }

  public static DrawIcon LoadWinFormsIcon(string installRoot, DrawIcon fallback)
  {
    var iconPath = SmartGuardPaths.BrandIcon(installRoot);
    if (_cachedWinFormsIcon is not null && string.Equals(_cachedIconPath, iconPath, StringComparison.OrdinalIgnoreCase))
      return (DrawIcon)_cachedWinFormsIcon.Clone();

    WinFormsLoadCountForTests++;
    if (File.Exists(iconPath))
    {
      try
      {
        var icon = new DrawIcon(iconPath, WinFormsTaskbarIconFrameSize, WinFormsTaskbarIconFrameSize);
        _cachedWinFormsIcon?.Dispose();
        _cachedWinFormsIcon = icon;
        _cachedIconPath = iconPath;
        return (DrawIcon)icon.Clone();
      }
      catch
      {
        // fall through
      }
    }

    return (DrawIcon)fallback.Clone();
  }

  public static ImageSource? LoadWpfImageSource(string installRoot, string? embeddedResourceUri = EmbeddedSettingsIconUri)
  {
    if (_cachedWpfImageSource is not null && _cachedWpfInstallRoot == installRoot)
      return _cachedWpfImageSource;

    var loaded = LoadWpfImageSourceCore(installRoot, embeddedResourceUri);
    if (loaded is not null)
    {
      _cachedWpfImageSource = loaded;
      _cachedWpfInstallRoot = installRoot;
    }

    return loaded;
  }

  private static ImageSource? LoadWpfImageSourceCore(string installRoot, string? embeddedResourceUri)
  {
    var filePath = SmartGuardPaths.BrandIcon(installRoot);
    if (File.Exists(filePath))
    {
      var fromFile = TryLoadWpfFromIconFile(filePath);
      if (fromFile is not null)
        return fromFile;
    }

    if (string.IsNullOrWhiteSpace(embeddedResourceUri))
      return null;

    try
    {
      var uri = new Uri(embeddedResourceUri, UriKind.Absolute);
      using var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
      if (stream is not null)
      {
        return TryLoadBestWpfFrame(stream);
      }
    }
    catch
    {
      // fall through
    }

    return null;
  }

  private static ImageSource? TryLoadWpfFromIconFile(string filePath)
  {
    try
    {
      using var stream = File.OpenRead(filePath);
      return TryLoadBestWpfFrame(stream);
    }
    catch
    {
      return null;
    }
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
