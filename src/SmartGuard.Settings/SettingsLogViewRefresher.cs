using System.Windows.Controls;

namespace SmartGuard.Settings;

internal static class SettingsLogViewRefresher
{
  // 诊断钩子：测试注入以追踪水平 offset 保存/恢复时序，产品路径为 null 无开销。
  internal static Action<string>? TraceForTests;

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
    Func<ScrollViewer?, double> resolveHorizontalOffset,
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
      TraceForTests?.Invoke("apply:no-change");
      statusLabel.Text = statusText;
      return;
    }

    var scrollViewer = resolveScrollViewer();
    var savedVerticalOffset = scrollViewer?.VerticalOffset ?? 0;
    var savedHorizontalOffset = resolveHorizontalOffset(scrollViewer);
    var wasAtTail = scrollViewer is null || LogViewScrollState.IsAtTail(scrollViewer);
    var scrollToTail = controller.FollowTail && wasAtTail;
    TraceForTests?.Invoke(
      $"apply:rebuild savedH={savedHorizontalOffset:F1} currentH={scrollViewer?.HorizontalOffset ?? -1:F1} " +
      $"mode={plan.Mode} forceRedraw={forceRedraw} scrollToTail={scrollToTail} sv={(scrollViewer is null ? "null" : "ok")}");

    listPresenter.Apply(plan);
    lastDisplayedLines = snapshot.DisplayLines.ToArray();
    statusLabel.Text = statusText;

    if (scrollToTail)
    {
      followTailCoordinator.ApplyScrollIfEnabled();
      RestoreHorizontalOffset(scrollViewer, savedHorizontalOffset);
      return;
    }

    if (scrollViewer is null)
      return;

    scrollViewer.UpdateLayout();
    if (!wasAtTail)
      scrollViewer.ScrollToVerticalOffset(savedVerticalOffset);

    RestoreHorizontalOffset(scrollViewer, savedHorizontalOffset);
  }

  private static void RestoreHorizontalOffset(ScrollViewer? scrollViewer, double savedHorizontalOffset)
  {
    if (scrollViewer is null || savedHorizontalOffset <= 0)
    {
      TraceForTests?.Invoke($"restore:skip savedH={savedHorizontalOffset:F1} sv={(scrollViewer is null ? "null" : "ok")}");
      return;
    }

    scrollViewer.Dispatcher.BeginInvoke(
      () =>
      {
        scrollViewer.UpdateLayout();
        if (scrollViewer.ScrollableWidth <= 0)
        {
          TraceForTests?.Invoke($"restore:noscroll savedH={savedHorizontalOffset:F1}");
          return;
        }

        var restored = Math.Min(savedHorizontalOffset, scrollViewer.ScrollableWidth);
        scrollViewer.ScrollToHorizontalOffset(restored);
        TraceForTests?.Invoke($"restore:done restored={restored:F1}");
      },
      System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  }
}
