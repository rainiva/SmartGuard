namespace SmartGuard.Settings.Tests;

public class LogViewIncrementalRefreshTests
{
    [Fact]
    public void Tail_growth_uses_append_plan_until_display_window_slides()
    {
        var previous = Enumerable.Range(0, 100).Select(i => $"[INFO] line {i}").ToList();
        var next = Enumerable.Range(0, 101).Select(i => $"[INFO] line {i}").ToList();

        LogViewUpdatePlanner.CreatePlan(previous, next, forceReplace: false)
            .Mode.Should().Be(LogViewUpdateMode.AppendTail);
    }

    [Fact]
    public void Sliding_window_after_slice_boundary_requires_full_replace()
    {
        var previous = Enumerable.Range(0, 500).Select(i => $"[INFO] line {i}").ToList();
        var next = Enumerable.Range(1, 500).Select(i => $"[INFO] line {i}").ToList();

        LogViewUpdatePlanner.CreatePlan(previous, next, forceReplace: false)
            .Mode.Should().Be(LogViewUpdateMode.ReplaceAll);
    }
}
