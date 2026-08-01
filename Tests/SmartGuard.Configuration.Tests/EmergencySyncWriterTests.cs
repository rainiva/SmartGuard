using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class EmergencySyncWriterTests
{
  [Fact]
  public void TryWrite_appends_lines_to_file()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "emergency-sync.log");
    try
    {
      var ok = EmergencySyncWriter.TryWrite(path, new[] { "line-a", "line-b" });
      ok.Should().BeTrue();
      File.ReadAllLines(path).Should().ContainInOrder("line-a", "line-b");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void TryWrite_returns_false_on_invalid_path()
  {
    var ok = EmergencySyncWriter.TryWrite(@"Z:\nonexistent\file.log", new[] { "line" });
    ok.Should().BeFalse();
  }

  [Fact]
  public void TryWrite_handles_empty_list()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "empty.log");
    try
    {
      EmergencySyncWriter.TryWrite(path, Array.Empty<string>()).Should().BeTrue();
      File.Exists(path).Should().BeFalse();
    }
    finally
    {
      if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
  }
}
