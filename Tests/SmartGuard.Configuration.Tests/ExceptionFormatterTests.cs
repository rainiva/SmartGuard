using System.Reflection;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class ExceptionFormatterTests
{
    [Fact]
    public void Format_includes_exception_type_and_message()
    {
        var ex = new InvalidOperationException("something broke");
        var text = ExceptionFormatter.Format(ex);
        text.Should().Contain("System.InvalidOperationException");
        text.Should().Contain("something broke");
    }

    [Fact]
    public void Format_includes_stack_trace_when_present()
    {
        Exception captured;
        try { throw new InvalidOperationException("boom"); }
        catch (Exception ex) { captured = ex; }
        var text = ExceptionFormatter.Format(captured);
        text.Should().Contain("StackTrace:");
    }

    [Fact]
    public void Format_recurses_into_inner_exception()
    {
        var ex = new InvalidOperationException("outer", new ArgumentException("inner-detail"));
        var text = ExceptionFormatter.Format(ex);
        text.Should().Contain("InnerException");
        text.Should().Contain("System.ArgumentException");
        text.Should().Contain("inner-detail");
    }

    [Fact]
    public void Format_expands_each_aggregate_inner_exception()
    {
        var ex = new AggregateException(
            new TimeoutException("t-out"),
            new UnauthorizedAccessException("denied"));
        var text = ExceptionFormatter.Format(ex);
        text.Should().Contain("System.TimeoutException");
        text.Should().Contain("System.UnauthorizedAccessException");
        text.Should().Contain("denied");
    }

    [Fact]
    public void Format_does_not_recurse_forever_on_circular_chain()
    {
        var a = new InvalidOperationException("cycle-a");
        var b = new InvalidOperationException("cycle-b");
        SetInnerException(a, b);
        SetInnerException(b, a);

        var act = () => ExceptionFormatter.Format(a);

        act.Should().NotThrow();
        var text = ExceptionFormatter.Format(a);
        text.Should().Contain("[循环异常链已截断]");
    }

    [Fact]
    public void Format_truncates_when_depth_exceeds_limit()
    {
        var head = new InvalidOperationException("depth-0");
        var current = head;
        for (var i = 1; i <= 40; i++)
        {
            var next = new InvalidOperationException($"depth-{i}");
            SetInnerException(current, next);
            current = next;
        }

        var act = () => ExceptionFormatter.Format(head);

        act.Should().NotThrow();
        var text = ExceptionFormatter.Format(head);
        text.Should().Contain("已截断");
    }

    private static void SetInnerException(Exception target, Exception inner)
    {
        var field = typeof(Exception).GetField("_innerException", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(target, inner);
    }
}
