using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogViewIdleDisplayPolicyTests
{
    [Fact]
    public void Resolve_uses_api_when_user_becomes_active()
    {
        LogViewIdleDisplayPolicy.Resolve(extrapolatedSeconds: 530, apiIdleSeconds: 8)
            .Should().Be(8);
    }

    [Fact]
    public void Resolve_keeps_extrapolated_when_user_still_idle()
    {
        LogViewIdleDisplayPolicy.Resolve(extrapolatedSeconds: 125, apiIdleSeconds: 120)
            .Should().Be(125);
    }

    [Fact]
    public void Resolve_uses_api_when_idle_is_near_zero()
    {
        LogViewIdleDisplayPolicy.Resolve(extrapolatedSeconds: 530, apiIdleSeconds: 0)
            .Should().Be(0);
    }
}
