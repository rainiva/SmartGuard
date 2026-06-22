using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class AppBrandIconCacheTests
{
    [Fact]
    public void LoadImageSource_reuses_cached_instance_for_same_install_root()
    {
        WpfStaTestHost.Run(() =>
        {
            var repoRoot = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(typeof(SettingsWindowController).Assembly.Location)!,
                "..", "..", "..", "..", ".."));
            var iconPath = Path.Combine(repoRoot, "lib", "SmartGuard.ico");
            if (!File.Exists(iconPath))
                return;

            AppBrandIcon.ClearCacheForTests();
            try
            {
                var first = AppBrandIcon.LoadImageSource(repoRoot);
                var second = AppBrandIcon.LoadImageSource(repoRoot);

                first.Should().NotBeNull();
                ReferenceEquals(first, second).Should().BeTrue();
            }
            finally
            {
                AppBrandIcon.ClearCacheForTests();
            }
        });
    }
}
