using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

public class GuardianExceptionStormHandlerTests
{
    [Fact]
    public void TrackAndLogException_logs_full_exception_chain_not_just_message()
    {
        var state = new GuardianLoopIterationState();
        string? logged = null;
        var handler = new GuardianExceptionStormHandler(state, (_, _, message) => logged = message);
        var ex = new InvalidOperationException("outer-msg", new ArgumentException("inner-msg"));

        handler.TrackAndLogException(ex, GuardConfig.CreateDefault(@"C:\SmartGuardRoot"));

        logged.Should().NotBeNull();
        logged!.Should().Contain("System.InvalidOperationException");
        logged.Should().Contain("System.ArgumentException");
        logged.Should().Contain("inner-msg");
    }
}
