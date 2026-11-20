using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayIconLoaderCacheTests
{
  [Fact]
  public void Load_returns_embedded_brand_icon_without_needing_a_caller_fallback()
  {
    TrayIconLoader.ResetCacheForTests();
    try
    {
      using var icon = TrayIconLoader.Load();
      icon.Should().NotBeNull();
      TrayIconLoader.LoadCountForTests.Should().Be(1);
    }
    finally
    {
      TrayIconLoader.ResetCacheForTests();
    }
  }

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

  [Fact]
  public async Task Concurrent_first_loads_share_one_embedded_icon_initialization()
  {
    TrayIconLoader.ResetCacheForTests();
    using var barrier = new Barrier(8);
    try
    {
      var loads = Enumerable.Range(0, barrier.ParticipantCount)
        .Select(_ => Task.Run(() =>
        {
          barrier.SignalAndWait(TimeSpan.FromSeconds(10));
          using var icon = TrayIconLoader.Load();
          icon.Should().NotBeNull();
        }))
        .ToArray();

      await Task.WhenAll(loads);
      TrayIconLoader.LoadCountForTests.Should().Be(1);
    }
    finally
    {
      TrayIconLoader.ResetCacheForTests();
    }
  }
}
