using FluentAssertions;
using SmartGuard.Engine.Worker;

namespace SmartGuard.Engine.Tests;

// Regression for the guardian process crash observed in the full gate:
// powercfg.exe /getactivescheme timed out after 5s escaped GuardianLoop's
// per-iteration try/catch (the plan probe on line ~97 sat outside it) and
// killed the whole engine with 0xE0434352 before any recovery could run.
public class GuardianPlanProbeTests
{
  [Fact]
  public void Probe_failure_keeps_last_known_guid_and_reports_instead_of_throwing()
  {
    var lastKnown = Guid.NewGuid();
    string? reported = null;

    var result = GuardianPlanProbe.ProbeOrKeep(
      () => throw new TimeoutException("powercfg.exe /getactivescheme timed out after 5s"),
      lastKnown,
      message => reported = message);

    result.Should().Be(lastKnown, "a transient powercfg timeout must not lose plan tracking");
    reported.Should().Contain("timed out after 5s");
  }

  [Fact]
  public void Probe_success_returns_fresh_guid_without_reporting()
  {
    var fresh = Guid.NewGuid();
    string? reported = null;

    var result = GuardianPlanProbe.ProbeOrKeep(
      () => fresh,
      Guid.NewGuid(),
      message => reported = message);

    result.Should().Be(fresh);
    reported.Should().BeNull();
  }
}
