using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartGuard.Settings;

internal sealed class LogViewportController
{
  private readonly Window _window;
  private readonly Func<ListBox?> _getListBox;
  private readonly Func<LogViewListPresenter?> _getPresenter;
  private readonly Func<CheckBox?> _getFollowCheckBox;
  private readonly Action<bool> _refreshLogView;

  private ScrollViewer? _scrollViewer;
  private bool _hooked;
  private bool _horizontalThumbHooked;
  private int _scrollGeneration;
  private bool _pendingUserScrollAwayFromTail;
  private double _preferredHorizontalOffset;
  private bool _hasPreferredHorizontalOffset;
  private double _lastUserHorizontalOffset;
  private bool _hasLastUserHorizontalOffset;
  private int? _pendingDeferredGeneration;
  private double _savedVerticalOffsetBeforeApply;
  private bool _wasAtTailBeforeApply;
  private bool _suppressCheckBoxSync;
  private bool _pendingInitialTailScroll;
  private bool _pendingInitialTailScrollInvalidated;
  private bool _dataApplyInProgress;
  private Action? _nearTopHandler;
  private bool _nearTopRaised;

  internal bool FollowLatest { get; private set; } = true;

  internal bool SuppressFollowAutoSync { get; private set; }

  internal bool PendingInitialTailScroll
  {
    get => _pendingInitialTailScroll;
    set
    {
      _pendingInitialTailScroll = value;
      if (!value)
        _pendingInitialTailScrollInvalidated = false;
    }
  }

  internal void MarkPendingInitialTailScroll()
  {
    _pendingInitialTailScroll = true;
    _pendingInitialTailScrollInvalidated = false;
  }

  internal void ClearPendingInitialTailScroll()
  {
    _pendingInitialTailScroll = false;
    _pendingInitialTailScrollInvalidated = false;
  }

  internal ScrollViewer? ScrollViewer => _scrollViewer;

  internal int ScrollGenerationForTests => _scrollGeneration;

  internal LogViewportController(
    Window window,
    Func<ListBox?> getListBox,
    Func<LogViewListPresenter?> getPresenter,
    Func<CheckBox?> getFollowCheckBox,
    Action<bool> refreshLogView)
  {
    _window = window;
    _getListBox = getListBox;
    _getPresenter = getPresenter;
    _getFollowCheckBox = getFollowCheckBox;
    _refreshLogView = refreshLogView;
  }

  internal void SetNearTopHandler(Action? handler)
  {
    _nearTopHandler = handler;
    _nearTopRaised = false;
  }

  internal void NotifyNearTopIfNeeded(ScrollViewer scrollViewer)
  {
    if (!LogViewScrollState.IsNearTop(scrollViewer))
    {
      _nearTopRaised = false;
      return;
    }

    if (_nearTopRaised)
      return;

    _nearTopRaised = true;
    _nearTopHandler?.Invoke();
  }

  internal void TryRaiseNearTopReached(ScrollViewer scrollViewer)
    => NotifyNearTopIfNeeded(scrollViewer);

  internal ScrollViewer? EnsureHooked(ListBox? listBox, LogViewListPresenter? presenter)
  {
    if (listBox is null)
      return _scrollViewer;

    if (_hooked)
      return _scrollViewer;

    _scrollViewer = ResolveScrollViewer(listBox, presenter);
    if (_scrollViewer is null)
      return null;

    ScrollBarAutoHide.Attach(_scrollViewer);
    _scrollViewer.ScrollChanged += OnScrollChanged;
    _scrollViewer.AddHandler(
      ScrollBar.ScrollEvent,
      new ScrollEventHandler(OnScrollBarScroll));
    _scrollViewer.AddHandler(
      UIElement.PreviewMouseWheelEvent,
      new MouseWheelEventHandler(OnPreviewMouseWheel),
      handledEventsToo: true);

    HookHorizontalThumbRecording();

    _hooked = true;
    return _scrollViewer;
  }

  internal void SyncFollowLatestFromUi()
  {
    if (_suppressCheckBoxSync)
      return;

    var checkBox = _getFollowCheckBox();
    if (checkBox is null)
      return;

    FollowLatest = checkBox.IsChecked == true;
  }

  internal void SyncFollowCheckBox()
  {
    var checkBox = _getFollowCheckBox();
    if (checkBox is null)
      return;

    _suppressCheckBoxSync = true;
    try
    {
      checkBox.IsChecked = FollowLatest;
    }
    finally
    {
      _suppressCheckBoxSync = false;
    }
  }

  internal void SetFollowLatest(bool enabled)
  {
    if (_suppressCheckBoxSync)
    {
      FollowLatest = enabled;
      if (!enabled)
        ClearPendingInitialTailScroll();

      return;
    }

    FollowLatest = enabled;
    if (!enabled)
    {
      ClearPendingInitialTailScroll();
      return;
    }

    _pendingUserScrollAwayFromTail = false;
    MarkPendingInitialTailScroll();
    _refreshLogView(true);
    ApplyFollowLatestIfEnabled();
  }

  internal void ScrollToTop()
  {
    _pendingUserScrollAwayFromTail = false;
    FollowLatest = false;
    SyncFollowCheckBox();
    ScrollToTopImmediate();
  }

  internal void ScrollToBottom()
  {
    _pendingUserScrollAwayFromTail = false;
    SuppressFollowAutoSync = true;
    try
    {
      ScrollToTailImmediate();
      FollowLatest = true;
      SyncFollowCheckBox();
    }
    finally
    {
      ReleaseFollowAutoSyncSuppression();
    }
  }

  internal void ScrollToEvent(string eventId)
  {
    var listBox = _getListBox();
    var presenter = _getPresenter();
    if (listBox is null || presenter is null)
      return;

    var index = presenter.FindIndexByEventId(eventId);
    if (index < 0)
      return;

    _pendingUserScrollAwayFromTail = false;
    FollowLatest = false;
    SyncFollowCheckBox();
    Interlocked.Increment(ref _scrollGeneration);
    listBox.UpdateLayout();

    if (listBox.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement element)
      element.BringIntoView();
    else if (index < listBox.Items.Count)
      listBox.ScrollIntoView(listBox.Items[index]);
  }

  internal void BringTableSelectionIntoView(DataGrid? table, object? selectedItem)
  {
    if (table is null || selectedItem is null)
      return;

    table.ScrollIntoView(selectedItem);
  }

  internal void ApplyFollowLatestIfEnabled(bool fromDataApply = false, bool fromRefreshPipeline = false)
  {
    if (fromRefreshPipeline
        && _scrollViewer is not null
        && _scrollViewer.ScrollableHeight > 0
        && !LogViewScrollState.IsAtTail(_scrollViewer))
    {
      ClearPendingInitialTailScroll();
      FollowLatest = false;
      SyncFollowCheckBox();
      return;
    }

    SyncFollowLatestFromUi();
    if (!FollowLatest)
    {
      ClearPendingInitialTailScroll();
      return;
    }

    if (!fromDataApply
        && !PendingInitialTailScroll
        && _scrollViewer is not null
        && !LogViewScrollState.IsAtTail(_scrollViewer))
    {
      FollowLatest = false;
      SyncFollowCheckBox();
      return;
    }

    MarkPendingInitialTailScroll();
    SuppressFollowAutoSync = true;
    ScrollToTail(deferred: true);
  }

  internal LogViewportAnchor? CaptureAnchor()
  {
    if (_scrollViewer is null || _getListBox() is not ListBox listBox || _getPresenter() is null)
      return null;

    listBox.UpdateLayout();
    var verticalOffset = _scrollViewer.VerticalOffset;

    for (var i = 0; i < listBox.Items.Count; i++)
    {
      if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
        continue;

      var top = GetItemTop(container);
      var bottom = top + container.ActualHeight;
      if (bottom <= verticalOffset + 0.5)
        continue;

      var eventId = _getPresenter()?.GetEventIdAt(i);
      if (string.IsNullOrWhiteSpace(eventId))
        break;

      return new LogViewportAnchor(eventId, verticalOffset - top, verticalOffset, i);
    }

    if (listBox.Items.Count == 0 || _scrollViewer.ExtentHeight <= 0)
      return null;

    var estimatedItemHeight = _scrollViewer.ExtentHeight / listBox.Items.Count;
    var estimatedIndex = (int)(verticalOffset / Math.Max(1, estimatedItemHeight));
    estimatedIndex = Math.Clamp(estimatedIndex, 0, listBox.Items.Count - 1);
    var estimatedEventId = _getPresenter()?.GetEventIdAt(estimatedIndex);
    if (string.IsNullOrWhiteSpace(estimatedEventId))
      return null;

    var estimatedTop = estimatedIndex * estimatedItemHeight;
    return new LogViewportAnchor(
      estimatedEventId,
      verticalOffset - estimatedTop,
      verticalOffset,
      estimatedIndex);
  }

  internal LogViewportAnchor? CaptureAnchorIfNeeded()
  {
    if (_scrollViewer is null)
      return null;

    if (FollowLatest
        && ((PendingInitialTailScroll && !_pendingInitialTailScrollInvalidated)
            || IsReliablyAtTail(_scrollViewer)))
      return null;

    return CaptureAnchor();
  }

  internal void RestoreAnchor(LogViewportAnchor anchor)
  {
    if (_scrollViewer is null || _getListBox() is not ListBox listBox)
      return;

    listBox.UpdateLayout();
    var newIndex = -1;
    for (var i = 0; i < listBox.Items.Count; i++)
    {
      if (string.Equals(_getPresenter()?.GetEventIdAt(i), anchor.EventId, StringComparison.Ordinal))
      {
        newIndex = i;
        break;
      }
    }

    if (newIndex < 0)
    {
      ScrollToVerticalOffsetImmediate(anchor.VerticalOffset);
      RestorePreferredHorizontalOffset();
      return;
    }

    var estimatedItemHeight = _scrollViewer.ExtentHeight / Math.Max(1, listBox.Items.Count);
    var target = anchor.VerticalOffset;

    if (listBox.ItemContainerGenerator.ContainerFromIndex(newIndex) is FrameworkElement container)
    {
      var preciseTarget = GetItemTop(container) + anchor.ItemPixelOffset;
      if (Math.Abs(preciseTarget - anchor.VerticalOffset) <= Math.Max(1, estimatedItemHeight))
        target = preciseTarget;
    }

    ScrollToVerticalOffsetImmediate(Math.Max(0, target));
    RestorePreferredHorizontalOffset();
  }

  internal void BeginDataApply()
  {
    EnsureHooked(_getListBox(), _getPresenter());
    _dataApplyInProgress = true;
    _savedVerticalOffsetBeforeApply = _scrollViewer?.VerticalOffset ?? 0;
    _wasAtTailBeforeApply = IsWasAtTailBeforeApply(
      _scrollViewer,
      _savedVerticalOffsetBeforeApply,
      _getListBox()?.Items.Count ?? 0);

    if (_scrollViewer is not null)
    {
      var horizontal = _scrollViewer.HorizontalOffset;
      if (horizontal > 0.01)
        RecordUserHorizontalOffset(horizontal);
    }

    if (!_wasAtTailBeforeApply)
      Interlocked.Increment(ref _scrollGeneration);
  }

  private static bool IsWasAtTailBeforeApply(
    ScrollViewer? scrollViewer,
    double savedVerticalOffset,
    int itemCount)
  {
    if (scrollViewer is null)
      return true;

    if (scrollViewer.ScrollableHeight > 0)
      return LogViewScrollState.IsNearTail(scrollViewer.ScrollableHeight, savedVerticalOffset);

    return itemCount == 0 && savedVerticalOffset <= 0.01;
  }

  internal void OnDataApplied(LogViewApplyResult applyResult, LogViewportAnchor? capturedAnchor)
  {
    try
    {
      EnsureHooked(_getListBox(), _getPresenter());
      if (_scrollViewer is null)
        return;

      if (FollowLatest
          && PendingInitialTailScroll
          && !_wasAtTailBeforeApply)
      {
        FollowLatest = false;
        ClearPendingInitialTailScroll();
        SyncFollowCheckBox();
        if (capturedAnchor is not null)
          RestoreAnchor(capturedAnchor);
        else
          RestoreSavedVerticalOffsetIfNeeded();
        RestorePreferredHorizontalOffset();
        return;
      }

      if (FollowLatest
          && PendingInitialTailScroll
          && _pendingInitialTailScrollInvalidated)
      {
        FollowLatest = false;
        ClearPendingInitialTailScroll();
        SyncFollowCheckBox();
        if (capturedAnchor is not null)
          RestoreAnchor(capturedAnchor);
        else
          RestoreSavedVerticalOffsetIfNeeded();
        RestorePreferredHorizontalOffset();
        return;
      }

      if (FollowLatest && PendingInitialTailScroll && _wasAtTailBeforeApply)
      {
        ApplyFollowLatestIfEnabled(fromDataApply: true);
        return;
      }

      if (FollowLatest && _wasAtTailBeforeApply)
      {
        ApplyFollowLatestIfEnabled(fromDataApply: true);
        return;
      }

      if (FollowLatest
          && applyResult is LogViewApplyResult.Appended or LogViewApplyResult.Replaced)
      {
        if (capturedAnchor is not null)
        {
          FollowLatest = false;
          ClearPendingInitialTailScroll();
          SyncFollowCheckBox();
          RestoreAnchor(capturedAnchor);
        }
        else
        {
          FollowLatest = false;
          ClearPendingInitialTailScroll();
          SyncFollowCheckBox();
          RestoreSavedVerticalOffsetIfNeeded();
        }

        RestorePreferredHorizontalOffset();
        return;
      }

      if (capturedAnchor is not null)
      {
        _scrollViewer.UpdateLayout();
        RestoreAnchor(capturedAnchor);
        RestorePreferredHorizontalOffset();
        return;
      }

      RestoreSavedVerticalOffsetIfNeeded();
      RestorePreferredHorizontalOffset();
    }
    finally
    {
      _dataApplyInProgress = false;
    }
  }

  internal double GetPreferredHorizontalOffset()
    => _hasLastUserHorizontalOffset
      ? _lastUserHorizontalOffset
      : _hasPreferredHorizontalOffset
        ? _preferredHorizontalOffset
        : _scrollViewer?.HorizontalOffset ?? 0;

  internal void ScrollToVerticalOffsetImmediate(double offset)
  {
    if (_scrollViewer is null)
      return;

    Interlocked.Increment(ref _scrollGeneration);
    _scrollViewer.UpdateLayout();
    _scrollViewer.ScrollToVerticalOffset(Math.Clamp(offset, 0, _scrollViewer.ScrollableHeight));
  }

  internal void ScrollToTopImmediate() => ScrollToVerticalOffsetImmediate(0);

  internal void HandleScrollChangedForTests(ScrollViewer scrollViewer)
    => OnScrollChanged(scrollViewer, null!);

  internal void BeginDeferredTailScrollForTests()
  {
    ScrollToTail(deferred: true, syncImmediate: false);
    _pendingDeferredGeneration = _scrollGeneration;
  }

  internal void CompleteDeferredTailScrollForTests(int generation)
    => RunDeferredIfCurrent(generation, ScrollToTailImmediate);

  private void OnScrollChanged(object sender, ScrollChangedEventArgs? e)
  {
    if (_scrollViewer is null)
      return;

    NotifyNearTopIfNeeded(_scrollViewer);

    // Negative VerticalChange during a deferred initial tail scroll usually means the
    // user forced the viewport back into history (including inside SuppressFollowAutoSync).
    // Ignore list-mutation noise while a data apply is in progress.
    if (PendingInitialTailScroll
        && !_dataApplyInProgress
        && e is not null
        && e.VerticalChange < -0.01)
    {
      _pendingInitialTailScrollInvalidated = true;
      Interlocked.Increment(ref _scrollGeneration);
    }

    if (SuppressFollowAutoSync || PendingInitialTailScroll)
      return;

    if (e is not null && Math.Abs(e.HorizontalChange) > 0.01)
    {
      var horizontalOffset = _scrollViewer.HorizontalOffset;
      _preferredHorizontalOffset = horizontalOffset;
      _hasPreferredHorizontalOffset = horizontalOffset > 0.01;
      if (horizontalOffset > 0.01)
        RecordUserHorizontalOffset(horizontalOffset);
    }

    var isAtTail = LogViewScrollState.IsAtTail(_scrollViewer);
    if (_pendingUserScrollAwayFromTail)
    {
      if (isAtTail)
        return;

      _pendingUserScrollAwayFromTail = false;
    }

    FollowLatest = isAtTail;
    SyncFollowCheckBox();
  }

  private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args)
  {
    if (_scrollViewer is null || args.Delta <= 0 || _scrollViewer.ScrollableHeight <= 0)
      return;

    CancelFollowLatestFromUserGesture();
  }

  private void OnScrollBarScroll(object sender, ScrollEventArgs args)
  {
    if (_scrollViewer is null || args.OriginalSource is not ScrollBar scrollBar)
      return;

    if (scrollBar.Orientation == Orientation.Horizontal)
    {
      if (args.ScrollEventType is ScrollEventType.ThumbTrack or ScrollEventType.ThumbPosition or ScrollEventType.EndScroll)
        RecordUserHorizontalOffset(_scrollViewer.HorizontalOffset);

      return;
    }

    if (args.ScrollEventType is ScrollEventType.LargeDecrement or ScrollEventType.SmallDecrement)
      CancelFollowLatestFromUserGesture();
    else if (args.ScrollEventType is ScrollEventType.ThumbTrack or ScrollEventType.ThumbPosition)
    {
      if (scrollBar.Orientation == Orientation.Vertical)
      {
        if (args.NewValue < _scrollViewer.VerticalOffset - 0.01)
          CancelFollowLatestFromUserGesture();
        else if (args.NewValue > _scrollViewer.VerticalOffset + 0.01
                 && !LogViewScrollState.IsAtTail(_scrollViewer))
          OnUserVerticalScroll();
      }
    }
  }

  private void CancelFollowLatestFromUserGesture()
  {
    _pendingUserScrollAwayFromTail = true;
    OnUserVerticalScroll();
  }

  private void OnUserVerticalScroll()
  {
    ClearPendingInitialTailScroll();
    SuppressFollowAutoSync = false;
    FollowLatest = false;
    SyncFollowCheckBox();
  }

  private void ScrollToTail(bool deferred, bool syncImmediate = true)
  {
    var generation = Interlocked.Increment(ref _scrollGeneration);
    SuppressFollowAutoSync = true;

    void Execute()
    {
      if (generation != _scrollGeneration || !FollowLatest)
        return;

      ScrollToTailImmediate();
      if (IsReliablyAtTail(_scrollViewer))
        ClearPendingInitialTailScroll();
    }

    if (syncImmediate)
      Execute();

    if (!deferred)
    {
      ReleaseFollowAutoSyncSuppression();
      return;
    }

    _window.Dispatcher.BeginInvoke(
      () =>
      {
        if (generation != _scrollGeneration || !FollowLatest)
        {
          ReleaseFollowAutoSyncSuppression();
          return;
        }

        RunDeferredIfCurrent(generation, () =>
        {
          Execute();
          _window.Dispatcher.BeginInvoke(
            () =>
            {
              if (generation != _scrollGeneration || !FollowLatest)
              {
                ReleaseFollowAutoSyncSuppression();
                return;
              }

              if (IsReliablyAtTail(_scrollViewer))
                ClearPendingInitialTailScroll();

              SyncFollowCheckBox();
              ReleaseFollowAutoSyncSuppression();
            },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        });
      },
      System.Windows.Threading.DispatcherPriority.Loaded);
  }

  private void ScrollToTailImmediate()
  {
    if (_scrollViewer is null || _getListBox() is not ListBox listBox)
      return;

    listBox.UpdateLayout();
    _scrollViewer.UpdateLayout();
    var horizontalOffset = _hasLastUserHorizontalOffset
      ? _lastUserHorizontalOffset
      : _hasPreferredHorizontalOffset
        ? _preferredHorizontalOffset
        : _scrollViewer.HorizontalOffset;

    if (listBox.Items.Count > 0)
      listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);

    _scrollViewer.UpdateLayout();
    _scrollViewer.ScrollToEnd();

    if (listBox.Items.Count > 0)
    {
      listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
      _scrollViewer.UpdateLayout();
      _scrollViewer.ScrollToEnd();
    }

    if (horizontalOffset > 0.01)
    {
      _scrollViewer.ScrollToHorizontalOffset(
        Math.Min(horizontalOffset, _scrollViewer.ScrollableWidth));
      _scrollViewer.UpdateLayout();
    }
  }

  private void RestoreSavedVerticalOffsetIfNeeded()
  {
    if (_scrollViewer is null || _wasAtTailBeforeApply)
      return;

    _scrollViewer.UpdateLayout();
    ScrollToVerticalOffsetImmediate(_savedVerticalOffsetBeforeApply);
  }

  private void RestorePreferredHorizontalOffset()
  {
    if (_scrollViewer is null)
      return;

    var saved = GetPreferredHorizontalOffset();
    if (saved <= 0.01)
      return;

    _scrollViewer.Dispatcher.BeginInvoke(
      () =>
      {
        if (_scrollViewer is null || _scrollViewer.ScrollableWidth <= 0)
          return;

        _scrollViewer.UpdateLayout();
        _scrollViewer.ScrollToHorizontalOffset(Math.Min(saved, _scrollViewer.ScrollableWidth));
      },
      System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  }

  private void HookHorizontalThumbRecording()
  {
    if (_horizontalThumbHooked || _scrollViewer is null)
      return;

    _scrollViewer.ApplyTemplate();
    if (_scrollViewer.Template?.FindName("PART_HorizontalScrollBar", _scrollViewer) is not ScrollBar scrollBar)
      return;

    scrollBar.ApplyTemplate();
    if (scrollBar.Template?.FindName("PART_Track", scrollBar) is not Track track)
      return;

    track.Thumb.AddHandler(
      Thumb.DragCompletedEvent,
      new DragCompletedEventHandler((_, _) => RecordUserHorizontalOffset(_scrollViewer.HorizontalOffset)),
      handledEventsToo: true);
    _horizontalThumbHooked = true;
  }

  private void RecordUserHorizontalOffset(double offset)
  {
    if (offset <= 0.01)
      return;

    _lastUserHorizontalOffset = offset;
    _hasLastUserHorizontalOffset = true;
    _preferredHorizontalOffset = offset;
    _hasPreferredHorizontalOffset = true;
  }

  private void RunDeferredIfCurrent(int generation, Action action)
  {
    if (generation != _scrollGeneration)
      return;

    action();
  }

  private void ReleaseFollowAutoSyncSuppression()
  {
    _window.Dispatcher.BeginInvoke(
      () => SuppressFollowAutoSync = false,
      System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  }

  private static bool IsReliablyAtTail(ScrollViewer? scrollViewer)
  {
    if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
      return false;

    return LogViewScrollState.IsAtTail(scrollViewer);
  }

  private static ScrollViewer? ResolveScrollViewer(ListBox? listBox, LogViewListPresenter? presenter)
  {
    if (listBox is null)
      return null;

    presenter?.EnsureScrollViewerResolved();
    return presenter?.ScrollViewer ?? LogViewListPresenter.FindScrollViewer(listBox);
  }

  private static double GetItemTop(FrameworkElement container)
  {
    DependencyObject? current = container;
    while (current is not null and not ScrollContentPresenter)
      current = VisualTreeHelper.GetParent(current);

    if (current is not ScrollContentPresenter contentPresenter)
      return 0;

    try
    {
      return container.TransformToVisual(contentPresenter).Transform(new Point(0, 0)).Y;
    }
    catch (InvalidOperationException)
    {
      return 0;
    }
  }
}
