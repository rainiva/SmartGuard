namespace SmartGuard.Engine.Infrastructure;

public sealed class BrightnessService
{
  private readonly WmiProbeProcess _probe;
  public BrightnessService() : this(WmiProbeProcess.Shared) { }
  internal BrightnessService(WmiProbeProcess probe) => _probe = probe;
  public int GetBrightness() => GetBrightness(CancellationToken.None);
  public int GetBrightness(CancellationToken cancellationToken) => _probe.ReadBrightness(cancellationToken);
  public void SetBrightness(int level) => SetBrightness(level, CancellationToken.None);
  public void SetBrightness(int level, CancellationToken cancellationToken) => _probe.SetBrightness(level, cancellationToken);

  public int RestoreWithRetry(int targetLevel, int maxAttempts, int delayMs)
  {
    return RestoreWithRetryAsync(targetLevel, maxAttempts, delayMs).GetAwaiter().GetResult();
  }

  public async Task<int> RestoreWithRetryAsync(int targetLevel, int maxAttempts, int delayMs,
    CancellationToken cancellationToken = default, Func<bool>? canMutate = null)
  {
    cancellationToken.ThrowIfCancellationRequested();
    const int budgetMs = 3000;
    if (targetLevel is < 0 or > 100 || maxAttempts is < 1 or > 5 || delayMs is < 0 or > 1000)
    {
      _probe.ReportRestoreDegraded("invalid-parameter", 0, budgetMs);
      return -1;
    }
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    budget.CancelAfter(budgetMs);
    var after = -1;
    try
    {
      for (var i = 0; i < maxAttempts; i++)
      {
        budget.Token.ThrowIfCancellationRequested();
        // Qualification and the total deadline are rechecked before each physical
        // operation; an unverified write never counts as successful restoration.
        if (canMutate?.Invoke() == false) return -1;
        SetBrightness(targetLevel, budget.Token);
        await Task.Delay(delayMs, budget.Token).ConfigureAwait(false);
        budget.Token.ThrowIfCancellationRequested();
        if (canMutate?.Invoke() == false) return -1;
        after = GetBrightness(budget.Token);
        budget.Token.ThrowIfCancellationRequested();
        if (after == targetLevel) return after;
      }
      _probe.ReportRestoreDegraded("restore-not-converged",
        (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, budgetMs);
      return after;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      _probe.ReportRestoreDegraded("budget-exhausted",
        (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, budgetMs);
      return -1;
    }
  }
}
