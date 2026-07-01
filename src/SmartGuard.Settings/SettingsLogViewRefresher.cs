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
    SettingsLogFollowTailCoordinator followTailCoordinator,
    Func<ScrollViewer?> resolveScrollViewer,
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

    var scrollViewer = resolveScrollViewer();
    var savedOffset = scrollViewer?.VerticalOffset ?? 0;
    var wasAtTail = scrollViewer is null || LogViewScrollState.IsAtTail(scrollViewer);
    var scrollToTail = controller.FollowTail && wasAtTail;

    listPresenter.Apply(plan);
    lastDisplayedLines = snapshot.DisplayLines.ToArray();
    statusLabel.Text = statusText;

    if (scrollToTail)
    {
      followTailCoordinator.ApplyScrollIfEnabled();
      return;
    }

    if (scrollViewer is null)
      return;

    scrollViewer.UpdateLayout();
    if (!wasAtTail)
      scrollViewer.ScrollToVerticalOffset(savedOffset);
  }
}
