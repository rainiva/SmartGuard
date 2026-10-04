using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class LoggingRepairArchitectureTests
{
  [Fact]
  public void Configuration_failure_event_and_repository_entry_have_one_registered_owner()
  {
    EventNameRegistry.IsRegistered("ConfigMutationFailed").Should().BeTrue();
    LogEventChannels.ForEventName("ConfigMutationFailed").Should().Be(LogChannelRegistry.Policy);
    typeof(GuardConfigRepository).GetMethod("AppendMutationFailureLog",
      new[] { typeof(string), typeof(string) }).Should().NotBeNull();
  }
}
