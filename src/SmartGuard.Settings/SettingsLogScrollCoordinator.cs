using System.Windows.Controls;

namespace SmartGuard.Settings;

internal sealed class SettingsLogScrollCoordinator
{
  private ScrollViewer? _scrollViewer;
  private bool _scrollHooked;

  internal ScrollViewer? EnsureHooked(
    ListBox? listView,
    LogViewListPresenter? listPresenter,
    SettingsLogFollowTailCoordinator followTailCoordinator)
  {
    if (listView is null || _scrollHooked)
      return _scrollViewer;

    _scrollViewer = Resolve(listView, listPresenter);
    if (_scrollViewer is null)
      return null;

    ScrollBarAutoHide.Attach(_scrollViewer);
    _scrollViewer.ScrollChanged += (_, _) => followTailCoordinator.OnScrollChanged(_scrollViewer);
    _scrollHooked = true;
    return _scrollViewer;
  }

  internal ScrollViewer? Resolve(ListBox? listView, LogViewListPresenter? listPresenter)
  {
    if (_scrollViewer is not null)
      return _scrollViewer;

    if (listView is null)
      return null;

    listPresenter?.EnsureScrollViewerResolved();
    _scrollViewer = listPresenter?.ScrollViewer ?? LogViewListPresenter.FindScrollViewer(listView);
    return _scrollViewer;
  }
}
