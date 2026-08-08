using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

  internal static void WireLogTableSorting(
    DataGrid logTable,
    Func<Task> toggleSortAsync,
    Func<bool>? canToggleSort = null)
  {
    ArgumentNullException.ThrowIfNull(logTable);
    ArgumentNullException.ThrowIfNull(toggleSortAsync);

    logTable.Sorting += (_, e) =>
    {
      e.Handled = true;
      if (canToggleSort is not null && !canToggleSort())
        return;

      _ = toggleSortAsync();
    };
  }

  internal static void WireLogTableWrapMode(DataGrid logTable, CheckBox wrapToggle)
  {
    ArgumentNullException.ThrowIfNull(logTable);
    ArgumentNullException.ThrowIfNull(wrapToggle);

    void ApplyRow(DataGridRow row)
    {
      var isWrapped = wrapToggle.IsChecked == true;
      row.Height = isWrapped ? double.NaN : 44;
      foreach (var textBlock in FindVisualChildren<TextBlock>(row))
        textBlock.TextWrapping = isWrapped ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    void Apply()
    {
      logTable.RowHeight = wrapToggle.IsChecked == true ? double.NaN : 44;
      foreach (var row in FindVisualChildren<DataGridRow>(logTable))
        ApplyRow(row);
    }

    wrapToggle.Checked += (_, _) => Apply();
    wrapToggle.Unchecked += (_, _) => Apply();
    logTable.LoadingRow += (_, e) => ApplyRow(e.Row);
    Apply();
  }

  internal static void WirePaginationCommands(
    Window window,
    Func<LogPaginationState> getState,
    Func<Task> firstPageAsync,
    Func<Task> previousPageAsync,
    Func<int, Task> goToPageAsync,
    Func<Task> nextPageAsync,
    Func<Task> lastPageAsync)
  {
    ArgumentNullException.ThrowIfNull(window);
    ArgumentNullException.ThrowIfNull(getState);
    ArgumentNullException.ThrowIfNull(firstPageAsync);
    ArgumentNullException.ThrowIfNull(previousPageAsync);
    ArgumentNullException.ThrowIfNull(goToPageAsync);
    ArgumentNullException.ThrowIfNull(nextPageAsync);
    ArgumentNullException.ThrowIfNull(lastPageAsync);

    AddBinding(LogPaginationCommands.FirstPage, _ => firstPageAsync());
    AddBinding(LogPaginationCommands.PreviousPage, _ => previousPageAsync());
    AddBinding(
      LogPaginationCommands.GoToPage,
      parameter => LogPaginationCommands.TryGetPageNumber(parameter, out var pageNumber)
        ? goToPageAsync(pageNumber)
        : Task.CompletedTask);
    AddBinding(LogPaginationCommands.NextPage, _ => nextPageAsync());
    AddBinding(LogPaginationCommands.LastPage, _ => lastPageAsync());

    void AddBinding(RoutedUICommand command, Func<object?, Task> execute)
    {
      window.CommandBindings.Add(new CommandBinding(
        command,
        (_, args) => _ = execute(args.Parameter),
        (_, args) =>
        {
          args.CanExecute = LogPaginationCommands.CanExecute(command, getState(), args.Parameter);
          args.Handled = true;
        }));
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

  private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
    where T : DependencyObject
  {
    var childCount = VisualTreeHelper.GetChildrenCount(root);
    for (var i = 0; i < childCount; i++)
    {
      var child = VisualTreeHelper.GetChild(root, i);
      if (child is T typedChild)
        yield return typedChild;

      foreach (var descendant in FindVisualChildren<T>(child))
        yield return descendant;
    }
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

  internal static void WireKeyboardCommands(
    Window window,
    Func<bool> isLogPageActive,
    Func<bool> isTextInputFocused,
    Action focusSearch,
    Action navigateNext,
    Action navigatePrevious,
    Action exportVisibleLog,
    Action scrollWindowTop,
    Action scrollWindowBottom,
    Action scrollHistoryStart,
    Action scrollHistoryEnd,
    Action closeTransientLayer)
  {
    Bind(LogWorkbenchCommands.FocusSearchCommand, focusSearch);
    Bind(LogWorkbenchCommands.NavigateNextCommand, navigateNext);
    Bind(LogWorkbenchCommands.NavigatePreviousCommand, navigatePrevious);
    Bind(LogWorkbenchCommands.ExportCommand, exportVisibleLog);
    Bind(LogWorkbenchCommands.ScrollWindowTopCommand, scrollWindowTop);
    Bind(LogWorkbenchCommands.ScrollWindowBottomCommand, scrollWindowBottom);
    Bind(LogWorkbenchCommands.ScrollHistoryStartCommand, scrollHistoryStart);
    Bind(LogWorkbenchCommands.ScrollHistoryEndCommand, scrollHistoryEnd);
    Bind(
      LogWorkbenchCommands.CloseTransientLayerCommand,
      () =>
      {
        var flyout = window.FindName("logMoreFlyout") as Border;
        var layer = window.FindName("logMoreFlyoutLayer") as Canvas;
        if (flyout?.Visibility == Visibility.Visible)
        {
          flyout.Visibility = Visibility.Collapsed;
          if (layer is not null)
            layer.IsHitTestVisible = false;
          return;
        }

        closeTransientLayer();
      });

    window.PreviewKeyDown += (_, e) =>
    {
      if (e.Key == Key.Escape
          && Keyboard.FocusedElement is ComboBox comboBox
          && comboBox.IsDropDownOpen)
        return;

      if (!isLogPageActive()
          || !LogWorkbenchCommands.TryResolve(
            e.Key,
            e.KeyboardDevice.Modifiers,
            isTextInputFocused(),
            out var command))
        return;

      command.Execute(null, window);
      e.Handled = true;
    };

    void Bind(RoutedCommand command, Action action)
      => window.CommandBindings.Add(new CommandBinding(command, (_, _) => action()));
  }

  internal static bool IsTextInputFocused()
  {
    var focused = Keyboard.FocusedElement;
    return focused is TextBoxBase or PasswordBox or ComboBox;
  }

  internal static void WireExceptionFoldEvents(ListBox listView, LogViewListPresenter listPresenter)
  {
    listView.MouseDoubleClick += (_, e) =>
    {
      if (listView.SelectedItem is not LogLineDisplayItem item || !item.IsExceptionRoot)
        return;

      listPresenter.ToggleExceptionFold(item.EventId);
      e.Handled = true;
    };
  }

  internal static void WireCorrelationEvents(ListBox listView, Action<LogLineDisplayItem> enterCorrelationView)
  {
    var menuItem = new MenuItem { Header = "查看本次操作" };
    menuItem.Click += (_, _) =>
    {
      if (listView.SelectedItem is LogLineDisplayItem item && item.HasCorrelationKey)
        enterCorrelationView(item);
    };

    var contextMenu = new ContextMenu();
    contextMenu.Opened += (_, _) =>
    {
      menuItem.IsEnabled = listView.SelectedItem is LogLineDisplayItem selected
        && selected.HasCorrelationKey
        && !selected.IsEmptyState;
    };
    contextMenu.Items.Add(menuItem);
    listView.ContextMenu = contextMenu;
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
