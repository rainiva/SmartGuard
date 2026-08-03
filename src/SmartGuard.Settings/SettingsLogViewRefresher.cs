using System.Windows.Controls;

namespace SmartGuard.Settings;

internal static class SettingsLogViewRefresher
{
  internal static void Apply(
    LogViewRefreshResult refreshResult,
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
    var snapshot = refreshResult.Snapshot;
    if (forceRedraw)
      forceRefreshCountForTests++;

    var idleRead = LogViewIdleReader.TryRead(root);
    if (idleRead.Seconds is not null)
      logIdleSeconds = idleRead.Seconds;
    logStatusMayBeStale = idleRead.StatusMayBeStale;

    var statusText = LogViewStatusTextBuilder.Build(snapshot, DateTime.Now, logIdleSeconds, logStatusMayBeStale);

    if (!forceRedraw
        && !refreshResult.ContentChanged
        && refreshResult.WindowDelta.IsEmpty
        && snapshot.EmptyStateMessage is null
        && !snapshot.LoadFailed)
    {
      statusLabel.Text = statusText;
      return;
    }

    viewport.BeginDataApply();
    var capturedAnchor = viewport.CaptureAnchorIfNeeded();
    LogViewApplyResult applyResult;
    if (forceRedraw
        || (snapshot.EmptyStateMessage is not null && snapshot.FilteredLines.Count == 0))
      applyResult = listPresenter.Apply(LogViewUpdatePlan.ReplaceAll(snapshot.DisplayLines));
    else
      applyResult = listPresenter.ApplyDelta(refreshResult.WindowDelta);

    lastDisplayedLines = snapshot.DisplayLines.ToArray();
    statusLabel.Text = statusText;
    viewport.OnDataApplied(applyResult, capturedAnchor);
    controller.FollowTail = viewport.FollowLatest;
  }
}
