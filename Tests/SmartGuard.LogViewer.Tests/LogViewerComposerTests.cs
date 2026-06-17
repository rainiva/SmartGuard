namespace SmartGuard.LogViewer.Tests;

public class LogViewerComposerTests
{
  [Fact]
  public void MergeChronologically_orders_lines_by_timestamp_across_sources()
  {
    const string primary = """
      [INFO] 2026-06-16 17:00:00 later main
      [INFO] 2026-06-16 15:00:00 early main
      """;
    const string fallback = """
      2026-06-16 16:00:00 startup line
      2026-06-16 14:54:58 uninstall old tasks
      """;

    var merged = LogViewerComposer.MergeChronologically(primary, fallback);
    var lines = merged.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    lines[0].Should().Contain("14:54:58");
    lines[1].Should().Contain("15:00:00");
    lines[2].Should().Contain("16:00:00");
    lines[3].Should().Contain("17:00:00");
    merged.Should().NotContain("--- fallback ---");
  }

  [Fact]
  public void MergeChronologically_returns_single_source_unchanged()
  {
    const string primary = "[INFO] 2026-06-16 15:00:00 only source";

    LogViewerComposer.MergeChronologically(primary, null).Should().Be(primary);
  }

  [Fact]
  public void TryParseTimestamp_supports_level_before_timestamp()
  {
    LogViewerComposer.TryParseTimestamp("[WARN] 2026-06-16 16:48:46 EXTERNAL", out var ts).Should().BeTrue();
    ts.Should().Be(new DateTime(2026, 6, 16, 16, 48, 46));
  }
}
