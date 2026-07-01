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

    controller.FollowTail = LogViewScrollState.IsAtTail(scrollViewer);
    SyncFollowTailToggle();
  }

  internal void ScrollToTop()
  {
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
    SuppressFollowTailAutoSync = true;
    try
    {
      ScrollLogViewToTail();
      var controller = _getController();
      if (controller is not null && _getFollowTailCheckBox()?.IsChecked != true)
        controller.FollowTail = false;
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

    PendingFollowTailInitialScroll = true;
    SuppressFollowTailAutoSync = true;
    ScrollLogViewToTail(deferred: true);
    _window.Dispatcher.BeginInvoke(
      () =>
      {
        ScrollLogViewToTail();
        _window.Dispatcher.BeginInvoke(
          () =>
          {
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

      var listBox = _getListBox();
      if (listBox is not null && listBox.Items.Count > 0)
        listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);

      scrollViewer.UpdateLayout();
      scrollViewer.ScrollToEnd();
    }

    ScrollNow();
    if (!deferred)
      return;

    _window.Dispatcher.BeginInvoke(
      ScrollNow,
      System.Windows.Threading.DispatcherPriority.Loaded);
  }

  private void ReleaseFollowTailAutoSyncSuppression()
  {
    _window.Dispatcher.BeginInvoke(
      () => SuppressFollowTailAutoSync = false,
      System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  }
}
