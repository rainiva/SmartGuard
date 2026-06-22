using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawIcon = System.Drawing.Icon;

namespace SmartGuard.Settings;

public static class AppBrandIcon
{
    private const string EmbeddedIconUri = "pack://application:,,,/Assets/SmartGuard.ico";
    private static ImageSource? _cachedImageSource;
    private static string? _cachedInstallRoot;

    internal static void ClearCacheForTests()
    {
        _cachedImageSource = null;
        _cachedInstallRoot = null;
    }

    public static ImageSource? LoadImageSource(string installRoot)
    {
        if (_cachedImageSource is not null && _cachedInstallRoot == installRoot)
            return _cachedImageSource;

        var loaded = LoadImageSourceCore(installRoot);
        if (loaded is not null)
        {
            _cachedImageSource = loaded;
            _cachedInstallRoot = installRoot;
        }

        return loaded;
    }

    private static ImageSource? LoadImageSourceCore(string installRoot)
    {
        var filePath = Path.Combine(installRoot, "lib", "SmartGuard.ico");
        if (File.Exists(filePath))
        {
            var fromFile = TryLoadFromIconFile(filePath);
            if (fromFile is not null)
                return fromFile;
        }

        try
        {
            var uri = new Uri(EmbeddedIconUri, UriKind.Absolute);
            using var stream = Application.GetResourceStream(uri)?.Stream;
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

    public static void ApplyTo(Window window, string installRoot)
    {
        var icon = LoadImageSource(installRoot);
        if (icon is not null)
            window.Icon = icon;

        var image = window.FindName("imgAppIcon") as System.Windows.Controls.Image;
        if (image is not null && icon is not null)
            image.Source = icon;
    }

    private static ImageSource? TryLoadFromIconFile(string filePath)
    {
        try
        {
            // Match tray loader: use the default .ico entry (typically 16x16/32x32),
            // not the largest high-color frame that BitmapFrame.Create picks for taskbar.
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
        using var bitmap = new System.Drawing.Bitmap(icon.Width, icon.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.DrawIcon(icon, new System.Drawing.Rectangle(0, 0, icon.Width, icon.Height));
        }

        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
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
