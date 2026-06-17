namespace SmartGuard.LogViewer.Tests;

public class LogTailReaderTests
{
  [Fact]
  public void ReadFromOffset_reads_text_after_offset()
  {
    var path = WriteTemp("tail.log", "alpha\nbeta\n");

    var slice = LogTailReader.ReadFromOffset(path, 6);

    slice.Length.Should().BeGreaterThan(0);
    slice.Text.Should().Be("beta\n");
  }

  [Fact]
  public void ReadFromOffset_returns_empty_when_file_missing()
  {
    var slice = LogTailReader.ReadFromOffset(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log"), 0);
    slice.Length.Should().Be(0);
    slice.Text.Should().BeEmpty();
  }

  [Fact]
  public void ReadRecentTail_reads_only_end_of_large_file()
  {
    var path = WriteTemp("large.log", "HEAD_START\n" + new string('A', 500_000) + "TAIL_MARKER\n");

    var slice = LogTailReader.ReadRecentTail(path, maxBytes: 4096);

    slice.Length.Should().BeGreaterThan(500_000);
    slice.Text.Should().Contain("TAIL_MARKER");
    slice.Text.Should().NotContain("HEAD_START");
  }

  [Fact]
  public void ReadInitialView_uses_recent_tail_instead_of_full_primary_file()
  {
    var primary = WriteTemp("primary.log", "HEAD_START\n" + new string('B', 120_000) + "[INFO] 2026-06-16 18:00:00 tail\n");
    var fallback = WriteTemp("fallback.log", "2026-06-16 16:00:00 startup\n");

    var initial = LogTailReader.ReadInitialView(primary, fallback, maxTailBytes: 4096);

    initial.FileLength.Should().BeGreaterThan(120_000);
    initial.Text.Should().Contain("18:00:00");
    initial.Text.Should().NotContain("HEAD_START");
    initial.Text.Should().Contain("16:00:00");
  }

  [Fact]
  public void ReadFullWithFallback_merges_primary_and_fallback_chronologically()
  {
    var primary = WriteTemp("primary.log", "[INFO] 2026-06-16 17:00:00 main log");
    var fallback = WriteTemp("fallback.log", "2026-06-16 16:00:00 startup log");

    var text = LogTailReader.ReadFullWithFallback(primary, fallback);

    text.Should().Contain("16:00:00");
    text.Should().Contain("17:00:00");
    text!.IndexOf("16:00:00", StringComparison.Ordinal).Should()
      .BeLessThan(text.IndexOf("17:00:00", StringComparison.Ordinal));
    text.Should().NotContain("--- fallback ---");
  }

  private static string WriteTemp(string name, string content)
  {
    var path = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"), name);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    return path;
  }
}
