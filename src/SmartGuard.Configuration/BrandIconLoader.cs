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
        var icon = new DrawIcon(iconPath);
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
        using var rawIcon = new DrawIcon(stream);
        using var icon = new DrawIcon(rawIcon, 32, 32);
        return FromIcon(icon);
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
      using var icon = new DrawIcon(filePath, 32, 32);
      return FromIcon(icon);
    }
    catch
    {
      return null;
    }
  }

  private static ImageSource FromIcon(DrawIcon icon)
  {
    using var bitmap = new Bitmap(icon.Width, icon.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(bitmap))
    {
      graphics.Clear(System.Drawing.Color.Transparent);
      graphics.DrawIcon(icon, new Rectangle(0, 0, icon.Width, icon.Height));
    }

    var bitmapData = bitmap.LockBits(
      new Rectangle(0, 0, bitmap.Width, bitmap.Height),
      System.Drawing.Imaging.ImageLockMode.ReadOnly,
      System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    try
    {
      var stride = bitmapData.Stride;
      var pixels = new byte[stride * bitmap.Height];
      System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);
      var source = BitmapSource.Create(
        bitmap.Width,
        bitmap.Height,
        96,
        96,
        PixelFormats.Bgra32,
        null,
        pixels,
        stride);
      source.Freeze();
      return source;
    }
    finally
    {
      bitmap.UnlockBits(bitmapData);
    }
  }
}
