using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class BrandIconLoaderTests
{
  [Fact]
  public void SaveEmbeddedToastIconToFile_writes_a_48px_png_for_shell_icon_uri()
  {
    var directory = Path.Combine(Path.GetTempPath(), "sg-toast-png-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "SmartGuard-toast.png");

    try
    {
      BrandIconLoader.SaveEmbeddedToastIconToFile(path);

      File.Exists(path).Should().BeTrue();
      var bytes = File.ReadAllBytes(path);
      bytes.Should().HaveCountGreaterThan(32);
      bytes.Take(8).Should().Equal(
        0x89, 0x50, 0x4E, 0x47,
        0x0D, 0x0A, 0x1A, 0x0A);

      ReadBigEndianInt32(bytes, 16).Should().Be(48);
      ReadBigEndianInt32(bytes, 20).Should().Be(48);
    }
    finally
    {
      if (File.Exists(path))
        File.Delete(path);

      if (Directory.Exists(directory))
        Directory.Delete(directory, false);
    }
  }

  private static int ReadBigEndianInt32(byte[] bytes, int offset)
    => (bytes[offset] << 24)
       | (bytes[offset + 1] << 16)
       | (bytes[offset + 2] << 8)
       | bytes[offset + 3];
}
