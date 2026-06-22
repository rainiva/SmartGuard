using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayIconLoaderCacheTests
{
    [Fact]
    public void Load_reuses_cached_icon_for_same_install_root()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var iconPath = Path.Combine(repoRoot, "lib", "SmartGuard.ico");
        if (!File.Exists(iconPath))
            return;

            TrayIconLoader.ResetCacheForTests();
            try
            {
                using var first = TrayIconLoader.Load(repoRoot);
                using var second = TrayIconLoader.Load(repoRoot);

            first.Should().NotBeNull();
            second.Should().NotBeNull();
            TrayIconLoader.LoadCountForTests.Should().Be(1);
        }
        finally
        {
            TrayIconLoader.ResetCacheForTests();
        }
    }
}
