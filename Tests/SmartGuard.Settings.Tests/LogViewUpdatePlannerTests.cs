namespace SmartGuard.Settings.Tests;

public class LogViewUpdatePlannerTests
{
    [Fact]
    public void CreatePlan_returns_no_change_when_lines_are_identical()
    {
        var previous = new[] { "[INFO] line 1", "[INFO] line 2" };
        var next = new[] { "[INFO] line 1", "[INFO] line 2" };

        var plan = LogViewUpdatePlanner.CreatePlan(previous, next, forceReplace: false);

        plan.Mode.Should().Be(LogViewUpdateMode.NoChange);
    }

    [Fact]
    public void CreatePlan_appends_tail_when_new_lines_extend_previous_prefix()
    {
        var previous = new[] { "[INFO] line 1", "[INFO] line 2" };
        var next = new[] { "[INFO] line 1", "[INFO] line 2", "[INFO] line 3" };

        var plan = LogViewUpdatePlanner.CreatePlan(previous, next, forceReplace: false);

        plan.Mode.Should().Be(LogViewUpdateMode.AppendTail);
        plan.AppendedLines.Should().Equal("[INFO] line 3");
        plan.AllLines.Should().Equal(next);
    }

    [Fact]
    public void CreatePlan_replaces_all_when_prefix_no_longer_matches()
    {
        var previous = new[] { "[INFO] line 1", "[INFO] line 2" };
        var next = new[] { "[WARN] line 9" };

        var plan = LogViewUpdatePlanner.CreatePlan(previous, next, forceReplace: false);

        plan.Mode.Should().Be(LogViewUpdateMode.ReplaceAll);
        plan.AllLines.Should().Equal(next);
    }

    [Fact]
    public void CreatePlan_replaces_all_when_force_replace_requested()
    {
        var previous = new[] { "[INFO] line 1" };
        var next = new[] { "[INFO] line 1", "[INFO] line 2" };

        var plan = LogViewUpdatePlanner.CreatePlan(previous, next, forceReplace: true);

        plan.Mode.Should().Be(LogViewUpdateMode.ReplaceAll);
    }

    [Fact]
    public void CreatePlan_replaces_all_when_previous_is_empty()
    {
        var plan = LogViewUpdatePlanner.CreatePlan([], ["[INFO] first"], forceReplace: false);

        plan.Mode.Should().Be(LogViewUpdateMode.ReplaceAll);
        plan.AllLines.Should().ContainSingle("[INFO] first");
    }
}
