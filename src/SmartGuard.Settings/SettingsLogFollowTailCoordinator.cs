using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal sealed class SettingsLogFollowTailCoordinator
{
  private readonly Window _window;
  private readonly Func<LogViewController?> _getController;
  private readonly Func<CheckBox?> _getFollowTailCheckBox;
  private readonly Func<ListBox?> _getListBox;
  private readonly Func<ScrollViewer?> _resolveScrollViewer;
  private readonly Action<bool> _refreshLogView;
  private bool _pendingUserScrollAwayFromTail;

  internal bool SuppressFollowTailAutoSync { get; private set; }

  internal bool PendingFollowTailInitialScroll { get; set; }

  internal SettingsLogFollowTailCoordinator(
    Window window,
    Func<LogViewController?> getController,
    Func<CheckBox?> getFollowTailCheckBox,
    Func<ListBox?> getListBox,
    Func<ScrollViewer?> resolveScrollViewer,
    Action<bool> refreshLogView)
  {
    _window = window;
    _getController = getController;
    _getFollowTailCheckBox = getFollowTailCheckBox;
    _getListBox = getListBox;
    _resolveScrollViewer = resolveScrollViewer;
    _refreshLogView = refreshLogView;
  }

  internal void OnScrollChanged(ScrollViewer scrollViewer)
  {
    var controller = _getController();
    if (controller is null || SuppressFollowTailAutoSync || PendingFollowTailInitialScroll)
      return;

    var isAtTail = LogViewScrollState.IsAtTail(scrollViewer);
    if (_pendingUserScrollAwayFromTail)
    {
      if (isAtTail)
        return;

      _pendingUserScrollAwayFromTail = false;
    }

    controller.FollowTail = isAtTail;
    SyncFollowTailToggle();
  }

  internal void OnUserScrollAwayFromTail()
  {
    _pendingUserScrollAwayFromTail = true;
    OnUserVerticalScroll();
  }

  internal void OnUserVerticalScroll()
  {
    PendingFollowTailInitialScroll = false;
    SuppressFollowTailAutoSync = false;

    var controller = _getController();
    if (controller is not null)
      controller.FollowTail = false;

    SyncFollowTailToggle();
  }

  internal void ScrollToTop()
  {
    _pendingUserScrollAwayFromTail = false;
    SuppressFollowTailAutoSync = true;
    try
    {
      var controller = _getController();
      if (controller is not null)
        controller.FollowTail = false;
      SyncFollowTailToggle();
      _resolveScrollViewer()?.ScrollToVerticalOffset(0);
    }
    finally
    {
      ReleaseFollowTailAutoSyncSuppression();
    }
  }

  internal void ScrollToBottom()
  {
    _pendingUserScrollAwayFromTail = false;
    SuppressFollowTailAutoSync = true;
    try
    {
      ScrollLogViewToTail();
      var controller = _getController();
      if (controller is not null)
        controller.FollowTail = true;
      SyncFollowTailToggle();
    }
    finally
    {
      ReleaseFollowTailAutoSyncSuppression();
    }
  }

  internal void SetFollowTail(bool enabled)
  {
    var controller = _getController();
    if (controller is null)
      return;

    controller.FollowTail = enabled;
    if (enabled)
    {
      _pendingUserScrollAwayFromTail = false;
      // Explicitly enabling follow-tail is an intentional jump to the tail,
      // exempt from the "keep the user's reading position" gate below.
      PendingFollowTailInitialScroll = true;
      _refreshLogView(true);
      ApplyScrollIfEnabled();
    }
  }

  internal void SyncFollowTailToggle()
  {
    var controller = _getController();
    var checkBox = _getFollowTailCheckBox();
    if (controller is null || checkBox is null)
      return;

    checkBox.IsChecked = controller.FollowTail;
  }

  internal void SyncFollowTailFromUi()
  {
    var controller = _getController();
    var checkBox = _getFollowTailCheckBox();
    if (controller is null || checkBox is null)
      return;

    controller.FollowTail = checkBox.IsChecked == true;
  }

  internal void ApplyScrollIfEnabled()
  {
    SyncFollowTailFromUi();
    var controller = _getController();
    if (controller is null || !controller.FollowTail)
    {
      PendingFollowTailInitialScroll = false;
      return;
    }

    // The checkbox can be stale-checked when a user scroll landed inside a
    // suppression window. Follow-tail only keeps following users who are
    // still near the tail; anyone reading history keeps their position and
    // the toggle self-heals to unchecked. Initial scrolls (page activation,
    // explicit re-check) are intentional jumps and skip this gate.
    if (!PendingFollowTailInitialScroll)
    {
      var currentScrollViewer = _resolveScrollViewer();
      if (currentScrollViewer is not null && !LogViewScrollState.IsAtTail(currentScrollViewer))
      {
        controller.FollowTail = false;
        SyncFollowTailToggle();
        return;
      }
    }

    PendingFollowTailInitialScroll = true;
    SuppressFollowTailAutoSync = true;
    ScrollLogViewToTail(deferred: true);
    _window.Dispatcher.BeginInvoke(
      () =>
      {
        if (!IsFollowTailEnabled())
          return;

        ScrollLogViewToTail();
        _window.Dispatcher.BeginInvoke(
          () =>
          {
            if (!IsFollowTailEnabled())
            {
              PendingFollowTailInitialScroll = false;
              ReleaseFollowTailAutoSyncSuppression();
              return;
            }

            var scrollViewer = _resolveScrollViewer();
            if (scrollViewer is not null && LogViewScrollState.IsAtTail(scrollViewer))
              PendingFollowTailInitialScroll = false;

            var followController = _getController();
            if (followController is not null && _getFollowTailCheckBox()?.IsChecked == true)
              followController.FollowTail = true;

            SyncFollowTailToggle();
            ReleaseFollowTailAutoSyncSuppression();
          },
          System.Windows.Threading.DispatcherPriority.ApplicationIdle);
      },
      System.Windows.Threading.DispatcherPriority.Loaded);
  }

  private void ScrollLogViewToTail(bool deferred = false)
  {
    void ScrollNow()
    {
      var scrollViewer = _resolveScrollViewer();
      if (scrollViewer is null)
        return;

      scrollViewer.UpdateLayout();

      // Follow-tail is a vertical concern: ScrollIntoView aligns the last
      // item's left edge, snapping the user's horizontal position to 0, so
      // restore the horizontal offset after reaching the tail.
      var horizontalOffset = scrollViewer.HorizontalOffset;

      var listBox = _getListBox();
      if (listBox is not null && listBox.Items.Count > 0)
        listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);

      scrollViewer.UpdateLayout();
      scrollViewer.ScrollToEnd();
      if (horizontalOffset > 0)
      {
        scrollViewer.ScrollToHorizontalOffset(horizontalOffset);
        scrollViewer.UpdateLayout();
      }
    }

    ScrollNow();
    if (!deferred)
      return;

    _window.Dispatcher.BeginInvoke(
      () =>
      {
        if (IsFollowTailEnabled())
          ScrollNow();
      },
      System.Windows.Threading.DispatcherPriority.Loaded);
  }

  private bool IsFollowTailEnabled()
    => _getController()?.FollowTail == true && _getFollowTailCheckBox()?.IsChecked == true;

  private void ReleaseFollowTailAutoSyncSuppression()
  {
    _window.Dispatcher.BeginInvoke(
      () => SuppressFollowTailAutoSync = false,
      System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  }
}
