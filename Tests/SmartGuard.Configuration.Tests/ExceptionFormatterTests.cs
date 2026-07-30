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
}
