using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class AppBrandIconTests
{
    private static void EnsureApplication() => WpfStaTestHost.EnsureApplication();

    [Fact]
    public void LoadImageSource_provides_a_window_icon_frame_large_enough_for_taskbar_big_icon()
    {
        RunOnSta(() =>
        {
            var iconPath = SettingsSourcePaths.BrandIconPath();

            File.Exists(iconPath).Should().BeTrue();

            var settingsIcon = AppBrandIcon.LoadImageSource();

            settingsIcon.Should().NotBeNull();
            settingsIcon!.Width.Should().BeGreaterThanOrEqualTo(64,
                "WPF must supply a large icon frame so ICON_BIG does not become a centered 32px bitmap inside a 64px taskbar canvas");
            settingsIcon.Height.Should().BeGreaterThanOrEqualTo(64);
        });
    }

    [Fact]
    public void LoadImageSource_reads_embedded_brand_icon()
    {
        RunOnSta(() =>
        {
            var iconPath = SettingsSourcePaths.BrandIconPath();

            File.Exists(iconPath).Should().BeTrue();

            var image = AppBrandIcon.LoadImageSource();
            image.Should().NotBeNull();
            image!.Width.Should().BeGreaterThan(0);
            image.Height.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void ApplyTo_sets_window_icon_from_embedded_brand_resource()
    {
        RunOnSta(() =>
        {
            EnsureApplication();

            var window = new Window();
            AppBrandIcon.ApplyTo(window);

            window.Icon.Should().NotBeNull();
            window.Icon!.Width.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void ApplyTo_preserves_the_canonical_big_taskbar_icon_shape_for_the_window_handle()
    {
        RunOnSta(() =>
        {
            EnsureApplication();
            var expectedSource = AppBrandIcon.LoadImageSource() as BitmapSource;
            expectedSource.Should().NotBeNull();
            var expectedCoverage = MeasureOpaqueCoverage(expectedSource!);

            var window = new Window
            {
                ShowInTaskbar = true,
                ShowActivated = false,
                Opacity = 0,
                WindowState = WindowState.Normal,
                Left = 0,
                Top = 0,
            };

            AppBrandIcon.ApplyTo(window);
            window.Show();
            window.UpdateLayout();
            AppBrandIcon.ApplyTo(window);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var handle = new WindowInteropHelper(window).Handle;
            handle.Should().NotBe(IntPtr.Zero);

            var bigIcon = Win32IconProbe.SendMessage(handle, Win32IconProbe.WM_GETICON, (IntPtr)Win32IconProbe.ICON_BIG, IntPtr.Zero);
            bigIcon.Should().NotBe(IntPtr.Zero);

            var source = Imaging.CreateBitmapSourceFromHIcon(bigIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.PixelWidth.Should().BeGreaterThanOrEqualTo(64);
            var actualCoverage = MeasureOpaqueCoverage(source);
            actualCoverage.WidthPct.Should().BeApproximately(expectedCoverage.WidthPct, 2.0,
                "the taskbar big icon should preserve the canonical embedded icon silhouette instead of falling back to a smaller centered bitmap");
            actualCoverage.HeightPct.Should().BeApproximately(expectedCoverage.HeightPct, 2.0,
                "the taskbar big icon should preserve the canonical embedded icon vertical bounds");
        });
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);

    private static (double WidthPct, double HeightPct) MeasureOpaqueCoverage(BitmapSource source)
    {
        var bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        var minX = bgra.PixelWidth;
        var maxX = -1;
        var minY = bgra.PixelHeight;
        var maxY = -1;
        for (var y = 0; y < bgra.PixelHeight; y++)
        {
            for (var x = 0; x < bgra.PixelWidth; x++)
            {
                var alpha = pixels[y * stride + x * 4 + 3];
                if (alpha <= 8)
                    continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        maxX.Should().BeGreaterThanOrEqualTo(minX);
        maxY.Should().BeGreaterThanOrEqualTo(minY);
        return (
            (maxX - minX + 1d) / bgra.PixelWidth * 100d,
            (maxY - minY + 1d) / bgra.PixelHeight * 100d);
    }

    private static class Win32IconProbe
    {
        internal const int WM_GETICON = 0x007F;
        internal const int ICON_BIG = 1;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
