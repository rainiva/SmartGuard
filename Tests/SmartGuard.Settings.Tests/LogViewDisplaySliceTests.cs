namespace SmartGuard.Settings.Tests;

public class LogViewDisplaySliceTests
{
    [Fact]
    public void Slice_returns_all_lines_when_under_limit()
    {
        var lines = Enumerable.Range(0, 10).Select(i => $"[INFO] line {i}").ToList();

        var result = LogViewDisplaySlice.Select(lines, maxLines: 100, preferTail: true);

        result.Lines.Should().HaveCount(10);
        result.IsTruncated.Should().BeFalse();
        result.TotalMatchedCount.Should().Be(10);
    }

    [Fact]
    public void Slice_keeps_tail_when_prefer_tail_and_over_limit()
    {
        var lines = Enumerable.Range(0, 5000).Select(i => $"[INFO] line {i}").ToList();

        var result = LogViewDisplaySlice.Select(lines, maxLines: 500, preferTail: true);

        result.Lines.Should().HaveCount(500);
        result.IsTruncated.Should().BeTrue();
        result.Lines[0].Should().Contain("line 4500");
        result.Lines[^1].Should().Contain("line 4999");
    }

    [Fact]
    public void Slice_keeps_head_when_not_prefer_tail_and_over_limit()
    {
        var lines = Enumerable.Range(0, 5000).Select(i => $"[INFO] line {i}").ToList();

        var result = LogViewDisplaySlice.Select(lines, maxLines: 500, preferTail: false);

        result.Lines.Should().HaveCount(500);
        result.Lines[0].Should().Contain("line 0");
        result.Lines[^1].Should().Contain("line 499");
    }
}
