using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class AppBrandIconCacheTests
{
    [Fact]
    public void LoadImageSource_reuses_cached_instance_for_same_install_root()
    {
        WpfStaTestHost.Run(() =>
        {
            var iconPath = SettingsSourcePaths.BrandIconPath();
            if (!File.Exists(iconPath))
                return;

            AppBrandIcon.ClearCacheForTests();
            try
            {
                var first = AppBrandIcon.LoadImageSource();
                var second = AppBrandIcon.LoadImageSource();

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
