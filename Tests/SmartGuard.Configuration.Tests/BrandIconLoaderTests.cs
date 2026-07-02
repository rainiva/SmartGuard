using System.IO;
using System.Drawing;

namespace SmartGuard.Configuration.Tests;

public class BrandIconLoaderTests
{
  [Fact]
  public void LoadWinFormsIcon_returns_32x32_taskbar_frame_not_largest_ico_frame()
  {
    BrandIconLoader.ResetCacheForTests();
    var repoRoot = Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory,
      "..", "..", "..", "..", ".."));
    var iconPath = Path.Combine(repoRoot, "lib", "SmartGuard.ico");
    File.Exists(iconPath).Should().BeTrue();

    using var fallback = new System.Drawing.Icon(iconPath, 16, 16);
    using var loaded = BrandIconLoader.LoadWinFormsIcon(repoRoot, fallback);

    loaded.Width.Should().Be(32, "WinForms tray must use the 32px ICO frame for taskbar parity");
    loaded.Height.Should().Be(32);
    loaded.Width.Should().NotBe(256, "must not load the default largest ICO frame");
  }

  [Fact]
  public void Brand_icon_ico_contains_taskbar_sizes()
  {
    var repoRoot = Path.GetFullPath(Path.Combine(
      AppContext.BaseDirectory,
      "..", "..", "..", "..", ".."));
    var iconPath = Path.Combine(repoRoot, "lib", "SmartGuard.ico");
    File.Exists(iconPath).Should().BeTrue();

    var sizes = ReadIconDirectorySizes(iconPath);
    sizes.Should().Contain(16);
    sizes.Should().Contain(32);
    sizes.Should().Contain(256);
    sizes.Count.Should().BeGreaterThanOrEqualTo(5,
      "tray/taskbar icons need multiple embedded sizes for crisp rendering");
  }

  private static List<int> ReadIconDirectorySizes(string iconPath)
  {
    var data = File.ReadAllBytes(iconPath);
    var count = BitConverter.ToUInt16(data, 4);
    var sizes = new List<int>(count);
    var offset = 6;

    for (var i = 0; i < count; i++)
    {
      var width = data[offset];
      sizes.Add(width == 0 ? 256 : width);
      offset += 16;
    }

    return sizes.Distinct().OrderBy(size => size).ToList();
  }
}
