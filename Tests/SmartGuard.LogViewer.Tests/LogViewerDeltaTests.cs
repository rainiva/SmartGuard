namespace SmartGuard.LogViewer.Tests;

public class LogViewerDeltaTests
{
  [Fact]
  public void PrepareForAppend_returns_delta_when_view_is_empty()
  {
    LogViewerDelta.PrepareForAppend(string.Empty, "2026-06-16 19:14:44 [INFO] line")
      .Should().Be("2026-06-16 19:14:44 [INFO] line");
  }

  [Fact]
  public void PrepareForAppend_inserts_newline_when_existing_text_and_delta_lack_line_break()
  {
    var existing = "[INFO] 2026-06-16 17:33:00 核心服务已在运行";
    var delta = "2026-06-16 19:14:44 [INFO] 电源事件：插电";

    LogViewerDelta.PrepareForAppend(existing, delta)
      .Should().Be(Environment.NewLine + delta);
  }

  [Fact]
  public void PrepareForAppend_keeps_delta_when_existing_text_already_ends_with_newline()
  {
    var existing = "[INFO] line one" + Environment.NewLine;
    var delta = "[INFO] line two";

    LogViewerDelta.PrepareForAppend(existing, delta).Should().Be(delta);
  }

  [Fact]
  public void PrepareForAppend_keeps_delta_when_delta_already_starts_with_newline()
  {
    var existing = "[INFO] line one";
    var delta = Environment.NewLine + "[INFO] line two";

    LogViewerDelta.PrepareForAppend(existing, delta).Should().Be(delta);
  }
}
