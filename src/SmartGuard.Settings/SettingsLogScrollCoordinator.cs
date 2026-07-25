using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SmartGuard.Settings;

internal sealed class SettingsLogScrollCoordinator
{
  private ScrollViewer? _scrollViewer;
  private bool _scrollHooked;
  private double? _horizontalCommittedOffset;
  private double? _verticalCommittedOffset;
  private bool _horizontalThumbTrackObserved;
  private bool _verticalThumbTrackObserved;
  private double _horizontalLastStableOffset;
  private double _verticalLastStableOffset;
  private double _horizontalObservedOffset;
  private double _verticalObservedOffset;
  private bool _horizontalThumbHooked;
  private bool _verticalThumbHooked;

  internal ScrollViewer? EnsureHooked(
    ListBox? listView,
    LogViewListPresenter? listPresenter,
    SettingsLogFollowTailCoordinator followTailCoordinator)
  {
    if (listView is null)
      return _scrollViewer;

    if (_scrollHooked)
    {
      EnsureThumbHandlersHooked(followTailCoordinator);
      return _scrollViewer;
    }

    _scrollViewer = Resolve(listView, listPresenter);
    if (_scrollViewer is null)
      return null;

    ScrollBarAutoHide.Attach(_scrollViewer);
    _scrollViewer.ScrollChanged += (_, _) => followTailCoordinator.OnScrollChanged(_scrollViewer);
    _scrollViewer.AddHandler(
      ScrollBar.ScrollEvent,
      new ScrollEventHandler((_, args) => OnUserScroll(args, followTailCoordinator)));
    EnsureThumbHandlersHooked(followTailCoordinator);
    _scrollHooked = true;
    return _scrollViewer;
  }

  private void OnUserScroll(
    ScrollEventArgs args,
    SettingsLogFollowTailCoordinator followTailCoordinator)
  {
    if (_scrollViewer is null || args.OriginalSource is not ScrollBar scrollBar)
      return;

    if (args.ScrollEventType == ScrollEventType.EndScroll)
    {
      RestoreCommittedOffset(scrollBar);
      return;
    }

    if (args.ScrollEventType is not (ScrollEventType.ThumbTrack or ScrollEventType.ThumbPosition))
      return;

    if (scrollBar.Orientation == Orientation.Horizontal)
    {
      _horizontalCommittedOffset = args.NewValue;
      _horizontalObservedOffset = args.NewValue;
      _horizontalThumbTrackObserved = true;
      _scrollViewer.ScrollToHorizontalOffset(args.NewValue);
      return;
    }

    _verticalCommittedOffset = args.NewValue;
    _verticalObservedOffset = args.NewValue;
    _verticalThumbTrackObserved = true;
    followTailCoordinator.OnUserVerticalScroll();
    _scrollViewer.ScrollToVerticalOffset(args.NewValue);
  }

  private void EnsureThumbHandlersHooked(SettingsLogFollowTailCoordinator followTailCoordinator)
  {
    if (_scrollViewer is null)
      return;

    _scrollViewer.ApplyTemplate();
    HookThumbHandler("PART_HorizontalScrollBar", followTailCoordinator, ref _horizontalThumbHooked);
    HookThumbHandler("PART_VerticalScrollBar", followTailCoordinator, ref _verticalThumbHooked);
  }

  private void HookThumbHandler(
    string partName,
    SettingsLogFollowTailCoordinator followTailCoordinator,
    ref bool hooked)
  {
    if (hooked
        || _scrollViewer?.Template?.FindName(partName, _scrollViewer) is not ScrollBar scrollBar)
      return;

    scrollBar.ApplyTemplate();
    if (scrollBar.Template?.FindName("PART_Track", scrollBar) is not Track track
        || track.Thumb is null)
      return;

    scrollBar.AddHandler(
      Thumb.DragStartedEvent,
      new DragStartedEventHandler((_, _) => OnThumbDragStarted(scrollBar, followTailCoordinator)),
      handledEventsToo: true);
    scrollBar.AddHandler(
      Thumb.DragDeltaEvent,
      new DragDeltaEventHandler((_, args) => OnThumbDragDelta(scrollBar, args, followTailCoordinator)),
      handledEventsToo: true);
    scrollBar.AddHandler(
      Thumb.DragCompletedEvent,
      new DragCompletedEventHandler((_, _) => RestoreCommittedOffset(scrollBar)),
      handledEventsToo: true);
    hooked = true;
  }

  private void OnThumbDragStarted(
    ScrollBar? scrollBar,
    SettingsLogFollowTailCoordinator followTailCoordinator)
  {
    if (_scrollViewer is null || scrollBar is null)
      return;

    if (scrollBar.Orientation == Orientation.Horizontal)
    {
      _horizontalCommittedOffset = _scrollViewer.HorizontalOffset;
      _horizontalLastStableOffset = _scrollViewer.HorizontalOffset;
      _horizontalThumbTrackObserved = false;
      return;
    }

    _verticalCommittedOffset = _scrollViewer.VerticalOffset;
    _verticalLastStableOffset = _scrollViewer.VerticalOffset;
    _verticalThumbTrackObserved = false;
    followTailCoordinator.OnUserVerticalScroll();
  }

  private void OnThumbDragDelta(
    ScrollBar? scrollBar,
    DragDeltaEventArgs args,
    SettingsLogFollowTailCoordinator followTailCoordinator)
  {
    if (_scrollViewer is null || scrollBar is null)
      return;

    scrollBar.ApplyTemplate();
    if (scrollBar.Template?.FindName("PART_Track", scrollBar) is not Track track)
      return;

    var delta = track.ValueFromDistance(args.HorizontalChange, args.VerticalChange);
    var nativeTrackObserved = scrollBar.Orientation == Orientation.Horizontal
      ? _horizontalThumbTrackObserved
      : _verticalThumbTrackObserved;
    var lastStableOffset = scrollBar.Orientation == Orientation.Horizontal
      ? _horizontalLastStableOffset
      : _verticalLastStableOffset;
    var observedOffset = scrollBar.Orientation == Orientation.Horizontal
      ? _horizontalObservedOffset
      : _verticalObservedOffset;
    SetThumbTrackObserved(scrollBar, false);

    var observedChange = observedOffset - lastStableOffset;
    var nativeMovedInDragDirection = nativeTrackObserved
      && Math.Abs(observedChange) > 0.01
      && Math.Sign(observedChange) == Math.Sign(delta);
    var target = nativeMovedInDragDirection
      ? observedOffset
      : Math.Clamp(lastStableOffset + delta, scrollBar.Minimum, scrollBar.Maximum);

    if (scrollBar.Orientation == Orientation.Horizontal)
    {
      _horizontalCommittedOffset = target;
      _horizontalLastStableOffset = target;
      _scrollViewer.ScrollToHorizontalOffset(target);
      return;
    }

    _verticalCommittedOffset = target;
    _verticalLastStableOffset = target;
    followTailCoordinator.OnUserVerticalScroll();
    _scrollViewer.ScrollToVerticalOffset(target);
  }

  private void RestoreCommittedOffset(ScrollBar scrollBar)
  {
    if (_scrollViewer is null || GetCommittedOffset(scrollBar) is not double offset)
      return;

    void Restore()
    {
      if (_scrollViewer is null)
        return;

      if (scrollBar.Orientation == Orientation.Horizontal)
        _scrollViewer.ScrollToHorizontalOffset(offset);
      else
        _scrollViewer.ScrollToVerticalOffset(offset);
    }

    Restore();
    _scrollViewer.Dispatcher.BeginInvoke(
      () =>
      {
        Restore();
        ClearCommittedOffset(scrollBar);
      },
      System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  }

  private double? GetCommittedOffset(ScrollBar scrollBar)
    => scrollBar.Orientation == Orientation.Horizontal
      ? _horizontalCommittedOffset
      : _verticalCommittedOffset;

  private void ClearCommittedOffset(ScrollBar scrollBar)
  {
    if (scrollBar.Orientation == Orientation.Horizontal)
      _horizontalCommittedOffset = null;
    else
      _verticalCommittedOffset = null;
  }

  private void SetThumbTrackObserved(ScrollBar scrollBar, bool observed)
  {
    if (scrollBar.Orientation == Orientation.Horizontal)
      _horizontalThumbTrackObserved = observed;
    else
      _verticalThumbTrackObserved = observed;
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
