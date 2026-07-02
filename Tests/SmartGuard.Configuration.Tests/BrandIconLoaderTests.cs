using System.IO;

namespace SmartGuard.Configuration.Tests;

public class BrandIconLoaderTests
{
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
