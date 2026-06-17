namespace SmartGuard.LogViewer.Tests;

public class LogLineDisplayFormatterTests
{
  [Fact]
  public void FormatLine_keeps_tag_before_timestamp_when_already_normalized()
  {
    var line = "[WARN] 2026-06-16 16:48:46 EXTERNAL: 计划被外部改为 平衡";
    LogLineDisplayFormatter.FormatLine(line).Should().Be(line);
  }

  [Fact]
  public void FormatLine_moves_bracket_tag_from_after_timestamp_to_front()
  {
    var input = "2026-06-16 16:00:13 [INFO] SmartGuard Engine 启动。";
    LogLineDisplayFormatter.FormatLine(input)
      .Should().Be("[INFO] 2026-06-16 16:00:13 SmartGuard Engine 启动。");
  }

  [Fact]
  public void FormatLine_normalizes_legacy_dash_separator_and_infers_warn_for_external()
  {
    var input = "2026-06-16 16:48:46 - EXTERNAL: 计划被外部改为 平衡 (381b...) | 下轮纠偏";
    LogLineDisplayFormatter.FormatLine(input)
      .Should().Be("[WARN] 2026-06-16 16:48:46 EXTERNAL: 计划被外部改为 平衡 (381b...) | 下轮纠偏");
  }

  [Fact]
  public void FormatLine_normalizes_legacy_plain_timestamp_and_infers_heart_for_monitoring()
  {
    var input = "2026-06-16 15:11:41 - [监控中] 活跃 | 计划正常 | 高性能 | 电量89% 插电";
    LogLineDisplayFormatter.FormatLine(input)
      .Should().Be("[HEART] 2026-06-16 15:11:41 [监控中] 活跃 | 计划正常 | 高性能 | 电量89% 插电");
  }

  [Fact]
  public void FormatLine_preserves_separator_lines()
  {
    LogLineDisplayFormatter.FormatLine("--- fallback ---").Should().Be("--- fallback ---");
  }

  [Fact]
  public void FormatText_applies_per_line_without_dropping_blank_lines()
  {
    var input = "2026-06-16 16:00:13 [INFO] one" + Environment.NewLine + Environment.NewLine
      + "2026-06-16 16:00:14 - EXTERNAL: two";
    var output = LogLineDisplayFormatter.FormatText(input);
    output.Should().Contain("[INFO] 2026-06-16 16:00:13 one");
    output.Should().Contain("[WARN] 2026-06-16 16:00:14 EXTERNAL: two");
    output.Split(Environment.NewLine, StringSplitOptions.None).Should().HaveCount(3);
  }
}
