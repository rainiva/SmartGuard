using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class PowerPlanOperationTests
{
  [Fact]
  public void Nonzero_exit_is_not_parsed_as_a_valid_plan()
  {
    var command = new PowerCfgCommandResult(
      true,
      false,
      1,
      string.Empty,
      "Access is denied.");

    var result = PowerCfgExecutor.ParseProbeResult(command);

    result.Succeeded.Should().BeFalse();
    result.FailureKind.Should().Be(PowerPlanFailureKind.CommandFailed);
    result.Plan.Should().BeNull();
  }

  [Fact]
  public async Task Apply_is_successful_only_when_followup_probe_matches_target()
  {
    var target = PowerPlanCatalogProvider.HighPerformancePlanGuid;
    var runner = new RecordingPowerCfgRunner(
      SetResult(exitCode: 0),
      ProbeResult(PowerPlanCatalogProvider.BalancedPlanGuid),
      ProbeResult(PowerPlanCatalogProvider.BalancedPlanGuid),
      ProbeResult(PowerPlanCatalogProvider.BalancedPlanGuid));

    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(
      target,
      runner.RunAsync,
      (_, _) => Task.CompletedTask,
      CancellationToken.None);

    result.Succeeded.Should().BeFalse();
    result.ObservedGuid.Should().Be(PowerPlanCatalogProvider.BalancedPlanGuid);
    result.FailureKind.Should().Be(PowerPlanFailureKind.VerificationMismatch);
    runner.Arguments.Should().Equal(
      $"/setactive {target:D}",
      "/getactivescheme",
      "/getactivescheme",
      "/getactivescheme");
  }

  [Fact]
  public async Task Apply_verification_accepts_bounded_delayed_convergence()
  {
    var target = PowerPlanCatalogProvider.HighPerformancePlanGuid;
    var delay = new RecordingDelay();
    var runner = new RecordingPowerCfgRunner(
      SetResult(exitCode: 0),
      ProbeResult(PowerPlanCatalogProvider.BalancedPlanGuid),
      ProbeResult(target));

    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(
      target,
      runner.RunAsync,
      delay.WaitAsync,
      CancellationToken.None);

    result.Succeeded.Should().BeTrue();
    delay.Requested.Should().Equal(TimeSpan.FromMilliseconds(250));
    runner.Arguments.Should().Equal(
      $"/setactive {target:D}",
      "/getactivescheme",
      "/getactivescheme");
  }

  private static PowerCfgCommandResult SetResult(int exitCode)
    => new(true, false, exitCode, string.Empty, exitCode == 0 ? string.Empty : "failed");

  private static PowerCfgCommandResult ProbeResult(Guid guid)
    => new(
      true,
      false,
      0,
      $"电源方案 GUID: {guid:D}  (test)",
      string.Empty);

  private sealed class RecordingPowerCfgRunner(params PowerCfgCommandResult[] results)
  {
    private readonly Queue<PowerCfgCommandResult> _results = new(results);

    public List<string> Arguments { get; } = [];

    public Task<PowerCfgCommandResult> RunAsync(string arguments, CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Arguments.Add(arguments);
      return Task.FromResult(_results.Dequeue());
    }
  }

  private sealed class RecordingDelay
  {
    public List<TimeSpan> Requested { get; } = [];

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Requested.Add(delay);
      return Task.CompletedTask;
    }
  }
}
