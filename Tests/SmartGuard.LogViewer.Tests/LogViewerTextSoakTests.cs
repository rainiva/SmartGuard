namespace SmartGuard.LogViewer.Tests;

public class LogViewerTextSoakTests
{
    [Fact]
    public void Simulated_long_running_appends_keep_display_text_bounded()
    {
        var text = string.Empty;
        const int maxBytes = LogViewerTextTrimmer.DefaultMaxCachedBytes;

        for (var i = 0; i < 5_000; i++)
        {
            var delta = $"[INFO] 2026-06-22 line {i}: {new string('x', 200)}\n";
            text = LogViewerDelta.PrepareForAppend(text, delta);
            text = LogViewerTextTrimmer.TrimToMaxBytes(text);
        }

        text.Length.Should().BeLessThanOrEqualTo(maxBytes);
        text.Should().Contain("line 4999");
    }
}
