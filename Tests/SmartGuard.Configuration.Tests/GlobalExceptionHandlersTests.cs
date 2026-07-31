using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class GlobalExceptionHandlersTests
{
    [Fact]
    public void Register_wires_process_wide_handlers_without_throwing()
    {
        var root = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        var act = () => GlobalExceptionHandlers.Register("Cli", root);
        act.Should().NotThrow();
    }

    [Fact]
    public void Register_is_idempotent_when_called_twice()
    {
        var root = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        var act = () =>
        {
            GlobalExceptionHandlers.Register("Cli", root);
            GlobalExceptionHandlers.Register("Cli", root);
        };
        act.Should().NotThrow();
    }
}
