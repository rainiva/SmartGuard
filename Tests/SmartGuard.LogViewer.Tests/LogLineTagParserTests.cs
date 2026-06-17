namespace SmartGuard.LogViewer.Tests;

public class LogLineTagParserTests
{
  [Fact]
  public void TryParse_extracts_tag_and_body_from_normalized_line()
  {
    const string line = "[WARN] 2026-06-16 16:48:46 EXTERNAL: 计划被外部改为 平衡";
    LogLineTagParser.TryParse(line, out var tag, out var body).Should().BeTrue();
    tag.Should().Be("WARN");
    body.Should().Be("2026-06-16 16:48:46 EXTERNAL: 计划被外部改为 平衡");
  }

  [Fact]
  public void TryParse_returns_false_for_separator_lines()
  {
    LogLineTagParser.TryParse("--- fallback ---", out _, out _).Should().BeFalse();
  }
}
