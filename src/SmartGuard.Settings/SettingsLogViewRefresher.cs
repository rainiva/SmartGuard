using System.Windows.Controls;

namespace SmartGuard.Settings;

internal static class SettingsLogViewRefresher
{
  internal static void Apply(
    LogViewSnapshot snapshot,
    LogViewController controller,
    LogViewListPresenter listPresenter,
    TextBlock statusLabel,
    string root,
    ref int? logIdleSeconds,
    ref bool logStatusMayBeStale,
    ref IReadOnlyList<string> lastDisplayedLines,
    ref int forceRefreshCountForTests,
    LogViewportController viewport,
    bool forceRedraw)
  {
    if (forceRedraw)
      forceRefreshCountForTests++;

    var idleRead = LogViewIdleReader.TryRead(root);
    if (idleRead.Seconds is not null)
      logIdleSeconds = idleRead.Seconds;
    logStatusMayBeStale = idleRead.StatusMayBeStale;

    var statusText = LogViewStatusTextBuilder.Build(snapshot, DateTime.Now, logIdleSeconds, logStatusMayBeStale);
    var plan = LogViewUpdatePlanner.CreatePlan(lastDisplayedLines, snapshot.DisplayLines, forceRedraw);

    if (!forceRedraw && !snapshot.ContentChanged && plan.Mode == LogViewUpdateMode.NoChange)
    {
      statusLabel.Text = statusText;
      return;
    }

    viewport.BeginDataApply();
    var capturedAnchor = viewport.CaptureAnchorIfNeeded();
    var applyResult = listPresenter.Apply(plan);
    lastDisplayedLines = snapshot.DisplayLines.ToArray();
    statusLabel.Text = statusText;
    viewport.OnDataApplied(applyResult, capturedAnchor);

    controller.FollowTail = viewport.FollowLatest;
  }
}
