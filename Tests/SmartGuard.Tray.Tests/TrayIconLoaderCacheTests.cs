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

    var installRoot = Path.Combine(Path.GetTempPath(), "sg-tray-icon-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(installRoot, "lib"));
    File.Copy(iconPath, Path.Combine(installRoot, "lib", "SmartGuard.ico"));

    TrayIconLoader.ResetCacheForTests();
    try
    {
      using var first = TrayIconLoader.Load(installRoot);
      var loadCountAfterFirst = TrayIconLoader.LoadCountForTests;
      using var second = TrayIconLoader.Load(installRoot);

      first.Should().NotBeNull();
      second.Should().NotBeNull();
      TrayIconLoader.LoadCountForTests.Should().Be(loadCountAfterFirst, "second load should reuse cached icon");
    }
    finally
    {
      TrayIconLoader.ResetCacheForTests();
      try { Directory.Delete(installRoot, true); } catch { }
    }
  }
}
