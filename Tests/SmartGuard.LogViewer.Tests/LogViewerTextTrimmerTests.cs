namespace SmartGuard.LogViewer.Tests;

public class LogViewerTextTrimmerTests
{
    [Fact]
    public void TrimToMaxBytes_returns_original_when_under_limit()
    {
        var text = "[INFO] line one\n[INFO] line two\n";

        LogViewerTextTrimmer.TrimToMaxBytes(text, maxBytes: 256).Should().Be(text);
    }

    [Fact]
    public void TrimToMaxBytes_keeps_tail_and_drops_head_on_line_boundary()
    {
        var head = new string('H', 300_000);
        var tail = "\n[INFO] 2026-06-22 10:00:00 keep this tail line";
        var text = head + tail;

        var trimmed = LogViewerTextTrimmer.TrimToMaxBytes(text, maxBytes: 262_144);

        trimmed.Should().NotContain(new string('H', 1000));
        trimmed.Should().Contain("keep this tail line");
        trimmed.Length.Should().BeLessThanOrEqualTo(262_144);
    }
}
