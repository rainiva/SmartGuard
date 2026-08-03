using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal static class SettingsLogPageHostWiring
{
  internal static void WireToolbarButtons(
    Window window,
    CheckBox? followTailCheckBox,
    LogViewportController logViewport,
    Action copyVisibleLog,
    Action exportVisibleLog,
    Action openLogFolder,
    Action forceRefreshLogView)
  {
    var btnLogMore = window.FindName("btnLogMore") as Button;
    var logMoreFlyoutLayer = window.FindName("logMoreFlyoutLayer") as Canvas;
    var logMoreFlyout = window.FindName("logMoreFlyout") as Border;
    var btnLogMoreCopy = window.FindName("btnLogMoreCopy") as Button;
    var btnLogMoreExport = window.FindName("btnLogMoreExport") as Button;
    var btnLogMoreOpenFolder = window.FindName("btnLogMoreOpenFolder") as Button;
    var btnLogScrollTop = window.FindName("btnLogScrollTop") as Button;
    var btnLogScrollBottom = window.FindName("btnLogScrollBottom") as Button;
    var btnLogRefresh = window.FindName("btnLogRefresh") as Button;

    void PositionLogMoreFlyout()
    {
      if (btnLogMore is null || logMoreFlyout is null || logMoreFlyoutLayer is null)
        return;

      logMoreFlyout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
      var buttonTopLeft = btnLogMore.TranslatePoint(new Point(0, 0), logMoreFlyoutLayer);
      var anchor = btnLogMore.TranslatePoint(
        new Point(btnLogMore.ActualWidth, btnLogMore.ActualHeight + 8),
        logMoreFlyoutLayer);
      var maxLeft = Math.Max(0, logMoreFlyoutLayer.ActualWidth - logMoreFlyout.DesiredSize.Width);
      var left = Math.Max(0, Math.Min(anchor.X - logMoreFlyout.DesiredSize.Width, maxLeft));
      var maxTop = Math.Max(0, logMoreFlyoutLayer.ActualHeight - logMoreFlyout.DesiredSize.Height);
      var top = anchor.Y;
      if (top > maxTop)
      {
        var aboveButtonTop = buttonTopLeft.Y - 8 - logMoreFlyout.DesiredSize.Height;
        top = Math.Max(0, aboveButtonTop);
      }

      Canvas.SetLeft(logMoreFlyout, left);
      Canvas.SetTop(logMoreFlyout, Math.Max(0, Math.Min(top, maxTop)));
    }

    void SetLogMoreFlyoutOpen(bool isOpen)
    {
      if (logMoreFlyout is not null)
      {
        logMoreFlyout.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        if (logMoreFlyoutLayer is not null)
          logMoreFlyoutLayer.IsHitTestVisible = isOpen;
        if (isOpen)
        {
          logMoreFlyout.UpdateLayout();
          PositionLogMoreFlyout();
        }
      }
    }

    if (btnLogMore is not null && logMoreFlyout is not null)
    {
      btnLogMore.Click += (_, _) =>
      {
        SetLogMoreFlyoutOpen(logMoreFlyout.Visibility != Visibility.Visible);
      };

      window.SizeChanged += (_, _) =>
      {
        if (logMoreFlyout.Visibility == Visibility.Visible)
          PositionLogMoreFlyout();
      };

      window.PreviewMouseDown += (_, e) =>
      {
        if (logMoreFlyout.Visibility != Visibility.Visible)
          return;

        var source = e.OriginalSource as DependencyObject;
        if (IsInSubtree(source, btnLogMore) || IsInSubtree(source, logMoreFlyout))
          return;

        SetLogMoreFlyoutOpen(false);
      };
      window.PreviewKeyDown += (_, e) =>
      {
        if (e.Key == Key.Escape)
          SetLogMoreFlyoutOpen(false);
      };
    }

    if (btnLogMoreCopy is not null) btnLogMoreCopy.Click += (_, _) =>
    {
      SetLogMoreFlyoutOpen(false);
      copyVisibleLog();
    };
    if (btnLogMoreExport is not null) btnLogMoreExport.Click += (_, _) =>
    {
      SetLogMoreFlyoutOpen(false);
      exportVisibleLog();
    };
    if (btnLogMoreOpenFolder is not null) btnLogMoreOpenFolder.Click += (_, _) =>
    {
      SetLogMoreFlyoutOpen(false);
      openLogFolder();
    };
    if (btnLogScrollTop is not null) btnLogScrollTop.Click += (_, _) => logViewport.ScrollToTop();
    if (btnLogScrollBottom is not null) btnLogScrollBottom.Click += (_, _) => logViewport.ScrollToBottom();
    if (btnLogRefresh is not null) btnLogRefresh.Click += (_, _) => forceRefreshLogView();
    if (followTailCheckBox is not null)
    {
      followTailCheckBox.Checked += (_, _) => logViewport.SetFollowLatest(true);
      followTailCheckBox.Unchecked += (_, _) => logViewport.SetFollowLatest(false);
    }
  }

  private static bool IsInSubtree(DependencyObject? source, DependencyObject ancestor)
  {
    var current = source;
    while (current is not null)
    {
      if (ReferenceEquals(current, ancestor))
        return true;

      current = current switch
      {
        Visual or Visual3D => VisualTreeHelper.GetParent(current),
        FrameworkContentElement contentElement => contentElement.Parent,
        _ => null,
      };
    }

    return false;
  }

  internal static void WireSearchAndFilterEvents(
    LogSearchFilterBar filterBar,
    SettingsLogSearchCoordinator searchCoordinator,
    ComboBox? timeRangeComboBox,
    UIElement? customRangePanel,
    TextBox? rangeStart,
    TextBox? rangeEnd,
    CheckBox? caseSensitiveCheckBox,
    Action refreshLogView,
    LogViewListPresenter? listPresenter = null,
    LogViewportController? viewport = null,
    Action<LogQueryCursor>? loadMatchPage = null)
  {
    filterBar.FiltersChanged += (_, _) => searchCoordinator.QueueSearchRefresh();
    if (timeRangeComboBox is not null)
    {
      timeRangeComboBox.SelectionChanged += (_, _) =>
      {
        SettingsLogSearchCoordinator.SyncCustomRangePanelVisibility(customRangePanel, timeRangeComboBox);
        refreshLogView();
      };
    }

    if (rangeStart is not null)
      rangeStart.TextChanged += (_, _) => searchCoordinator.QueueCustomRangeRefresh();
    if (rangeEnd is not null)
      rangeEnd.TextChanged += (_, _) => searchCoordinator.QueueCustomRangeRefresh();
    if (caseSensitiveCheckBox is not null)
    {
      caseSensitiveCheckBox.Checked += (_, _) => refreshLogView();
      caseSensitiveCheckBox.Unchecked += (_, _) => refreshLogView();
    }

    WireMatchNavigationKeys(filterBar, searchCoordinator, listPresenter, viewport, loadMatchPage);
  }

  internal static void WireMatchNavigationKeys(
    LogSearchFilterBar filterBar,
    SettingsLogSearchCoordinator searchCoordinator,
    LogViewListPresenter? listPresenter,
    LogViewportController? viewport,
    Action<LogQueryCursor>? loadMatchPage = null)
  {
    void Navigate(LogMatchNavigationDirection direction)
      => searchCoordinator.NavigateMatch(direction, listPresenter, viewport, loadMatchPage);

    filterBar.PreviewKeyDown += (_, e) =>
    {
      if (LogMatchNavigationCommands.TryHandleKeyDown(e, Navigate))
        e.Handled = true;
    };
  }
}
