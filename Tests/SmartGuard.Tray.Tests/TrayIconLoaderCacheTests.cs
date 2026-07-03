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
    var iconPath = Path.Combine(repoRoot, "assets", "branding", "SmartGuard.ico");
    if (!File.Exists(iconPath))
      return;

    TrayIconLoader.ResetCacheForTests();
    try
    {
      using var first = TrayIconLoader.Load();
      var loadCountAfterFirst = TrayIconLoader.LoadCountForTests;
      using var second = TrayIconLoader.Load();

      first.Should().NotBeNull();
      second.Should().NotBeNull();
      TrayIconLoader.LoadCountForTests.Should().Be(loadCountAfterFirst, "second load should reuse cached icon");
    }
    finally
    {
      TrayIconLoader.ResetCacheForTests();
    }
  }
}
