using SmartGuard.Adversary;

namespace SmartGuard.Adversary.Tests;

public class AdversaryResultContractTests
{
  private static readonly AdversaryRequest Request = new([EffectTier.T0, EffectTier.T1]);

  [Theory]
  [InlineData(ResultState.RestoreFailed, 30)]
  [InlineData(ResultState.SelfCheckFailed, 21)]
  [InlineData(ResultState.InfrastructureFailed, 20)]
  [InlineData(ResultState.Blocked, 40)]
  [InlineData(ResultState.FlakyFinding, 11)]
  [InlineData(ResultState.ConfirmedFinding, 10)]
  public void Failure_cannot_be_hidden_by_another_passed_layer(ResultState state, int exit)
  {
    var result = ParentResultModel.Evaluate(Request,
      [new(EffectTier.T0, ResultState.Passed, 1), new(EffectTier.T1, state, 1)], true);
    Assert.Equal(new ParentEvaluation(state, exit), result);
  }

  [Fact]
  public void Restore_failure_takes_priority_over_blocked_and_findings()
  {
    var result = ParentResultModel.Evaluate(new([EffectTier.T0, EffectTier.T1, EffectTier.T2]),
      [new(EffectTier.T0, ResultState.ConfirmedFinding, 1), new(EffectTier.T1, ResultState.Blocked, 0),
       new(EffectTier.T2, ResultState.RestoreFailed, 0)], false, true);
    Assert.Equal(new ParentEvaluation(ResultState.RestoreFailed, 30), result);
  }

  [Theory]
  [InlineData(ResultState.ConfirmedFinding)]
  [InlineData(ResultState.FlakyFinding)]
  public void Finding_without_any_executed_case_is_contract_failure(ResultState state)
    => Assert.Equal(20, ParentResultModel.Evaluate(new([EffectTier.T0]),
      [new(EffectTier.T0, state, 0)], true).ExitCode);

  [Fact]
  public void Missing_requested_layer_is_blocked()
    => Assert.Equal(40, ParentResultModel.Evaluate(Request, [new(EffectTier.T0, ResultState.Passed, 1)], true).ExitCode);

  [Fact]
  public void Unrequested_notrun_is_distinct_and_does_not_block_a_complete_model()
    => Assert.Equal(new ParentEvaluation(ResultState.Passed, 0), ParentResultModel.Evaluate(Request,
      [new(EffectTier.T0, ResultState.Passed, 1), new(EffectTier.T1, ResultState.Passed, 1),
       new(EffectTier.T2, ResultState.NotRun, 0)], true));

  [Fact]
  public void Requested_notrun_is_blocked()
    => Assert.Equal(40, ParentResultModel.Evaluate(Request,
      [new(EffectTier.T0, ResultState.Passed, 1), new(EffectTier.T1, ResultState.NotRun, 0)], true).ExitCode);

  [Theory]
  [InlineData(0, 0, false, 20)]
  [InlineData(1, 1, false, 40)]
  [InlineData(1, 1, true, 41)]
  [InlineData(-1, 0, false, 20)]
  [InlineData(1, -1, false, 20)]
  public void Zero_cases_remaining_work_and_budget_abort_cannot_pass(int executed, int remaining, bool budget, int exit)
    => Assert.Equal(exit, ParentResultModel.Evaluate(new([EffectTier.T0]),
      [new(EffectTier.T0, ResultState.Passed, executed, remaining)], true, budget).ExitCode);

  [Fact]
  public void Uncalibrated_success_is_self_check_failure()
    => Assert.Equal(21, ParentResultModel.Evaluate(new([EffectTier.T0]),
      [new(EffectTier.T0, ResultState.Passed, 1)], false).ExitCode);

  [Fact]
  public void Duplicate_layer_is_contract_failure()
    => Assert.Equal(20, ParentResultModel.Evaluate(Request,
      [new(EffectTier.T0, ResultState.Passed, 1), new(EffectTier.T0, ResultState.Passed, 1)], true).ExitCode);

  [Fact]
  public void Unrequested_execution_is_contract_failure()
    => Assert.Equal(20, ParentResultModel.Evaluate(new([EffectTier.T0]),
      [new(EffectTier.T0, ResultState.Passed, 1), new(EffectTier.T1, ResultState.Passed, 1)], true).ExitCode);

  [Fact]
  public async Task Skeleton_reports_requested_work_unexecuted_without_final_verdict()
  {
    var observation = await AdversaryCommand.RunAsync(Request, CancellationToken.None);
    Assert.Equal(2, observation.Layers.Length);
    Assert.All(observation.Layers, layer => { Assert.Equal(0, layer.Executed); Assert.Equal(100, layer.Remaining); Assert.NotEmpty(layer.BlockReason); });
    Assert.DoesNotContain(typeof(AdversaryObservation).GetProperties(), p => p.Name is "Result" or "ExitCode" or "TrustLevel");
  }

  [Fact]
  public async Task Cancellation_prevents_work()
  {
    using var source = new CancellationTokenSource();
    source.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AdversaryCommand.RunAsync(Request, source.Token));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(2041)]
  public async Task Case_budget_is_bounded(int maxCases)
    => await Assert.ThrowsAsync<ArgumentException>(() => AdversaryCommand.RunAsync(new([EffectTier.T0], maxCases), CancellationToken.None));

  [Fact]
  public async Task Unknown_tier_is_rejected()
    => await Assert.ThrowsAsync<ArgumentException>(() => AdversaryCommand.RunAsync(new([(EffectTier)99]), CancellationToken.None));
}
