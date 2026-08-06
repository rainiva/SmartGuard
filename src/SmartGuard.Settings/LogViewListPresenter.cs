using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using SmartGuard.Configuration;
using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

public enum LogViewApplyResult
{
    Unchanged,
    Appended,
    Replaced,
}

public sealed class LogViewListPresenter
{
    private readonly ObservableCollection<LogLineDisplayItem> _items = new();
    private readonly List<LogEvent> _sourceEvents = [];
    private readonly LogExceptionFoldState _exceptionFoldState = new();
    private ListBox? _listBox;

    public ReadOnlyObservableCollection<LogLineDisplayItem> Items { get; }

    public ScrollViewer? ScrollViewer { get; private set; }

    public LogViewListPresenter()
    {
        Items = new ReadOnlyObservableCollection<LogLineDisplayItem>(_items);
    }

    public void Attach(ListBox listBox)
    {
        _listBox = listBox;
        ConfigureListBox(listBox);
        listBox.ItemsSource = _items;
        listBox.ItemTemplate = CreateItemTemplate();

        if (listBox.IsLoaded)
            AttachScrollViewer(listBox);
        else
            listBox.Loaded += OnListBoxLoaded;
    }

    public static void ConfigureListBox(ListBox listBox)
    {
        VirtualizingPanel.SetIsVirtualizing(listBox, true);
        VirtualizingPanel.SetVirtualizationMode(listBox, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(listBox, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(listBox, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
        listBox.BorderThickness = new Thickness(0);
        listBox.Background = Brushes.Transparent;
        listBox.Padding = new Thickness(12);
        listBox.FontFamily = new FontFamily("Consolas, Microsoft YaHei Mono");
        listBox.FontSize = 12;
        listBox.Focusable = true;
        KeyboardNavigation.SetIsTabStop(listBox, true);
        AutomationProperties.SetName(listBox, "日志列表");
        AutomationProperties.SetHelpText(
            listBox,
            "使用 Home/End 浏览当前窗口，Ctrl+Home/Ctrl+End 浏览历史首尾");
        listBox.FocusVisualStyle = CreateFocusVisualStyle();

        var itemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        listBox.ItemsPanel = itemsPanel;
    }

    public LogViewApplyResult Apply(LogViewUpdatePlan plan)
    {
        return plan.Mode switch
        {
            LogViewUpdateMode.NoChange => LogViewApplyResult.Unchanged,
            LogViewUpdateMode.AppendTail => AppendTail(plan),
            LogViewUpdateMode.ReplaceAll => ReplaceAll(plan),
            _ => LogViewApplyResult.Unchanged,
        };
    }

    public LogViewApplyResult ApplyDelta(LogWindowDelta delta)
    {
        if (delta.IsEmpty)
            return LogViewApplyResult.Unchanged;

        if (delta.IsInitialize)
        {
            _sourceEvents.Clear();
            _sourceEvents.AddRange(delta.InitializeEvents);
            ReplaceItemsWithoutReset(ProjectCurrentItems());
            NotifyContentChanged();
            return LogViewApplyResult.Replaced;
        }

        for (var i = 0; i < delta.RemovedHeadCount && _sourceEvents.Count > 0; i++)
            _sourceEvents.RemoveAt(0);

        for (var i = 0; i < delta.RemovedTailCount && _sourceEvents.Count > 0; i++)
            _sourceEvents.RemoveAt(_sourceEvents.Count - 1);

        if (delta.PrependedEvents.Count > 0)
        {
            for (var i = delta.PrependedEvents.Count - 1; i >= 0; i--)
                _sourceEvents.Insert(0, delta.PrependedEvents[i]);
        }

        if (delta.AppendedEvents.Count > 0)
            _sourceEvents.AddRange(delta.AppendedEvents);

        ReplaceItemsWithoutReset(ProjectCurrentItems());
        NotifyContentChanged();

        return delta.AppendedEvents.Count > 0 || delta.PrependedEvents.Count > 0
            ? LogViewApplyResult.Appended
            : LogViewApplyResult.Replaced;
    }

    public void ToggleExceptionFold(string rootEventId)
    {
        var expanded = _exceptionFoldState.Toggle(rootEventId);
        var rootIndex = FindIndexByEventId(rootEventId);
        if (rootIndex < 0)
            return;

        var group = LogExceptionGroupProjector.BuildGroups(_sourceEvents)
            .FirstOrDefault(candidate => candidate.RootEvent.EventId == rootEventId);
        if (group is null || !group.HasFoldableContent)
            return;

        _items[rootIndex] = LogLineDisplayItem.FromExceptionGroup(group, expanded, includeContinuations: expanded);

        if (expanded)
        {
            var insertAt = rootIndex + 1;
            if (group.IsStructured && group.RootEvent.Exception is not null)
            {
                foreach (var line in group.RootEvent.Exception.FormatExpandedLines().Skip(1))
                    _items.Insert(insertAt++, LogLineDisplayItem.FromStructuredExceptionLine(group, line));
            }
            else
            {
                foreach (var continuation in group.ContinuationEvents)
                    _items.Insert(insertAt++, LogLineDisplayItem.FromExceptionContinuation(group, continuation));
            }
        }
        else
        {
            for (var i = _items.Count - 1; i > rootIndex; i--)
            {
                if (_items[i].IsExceptionContinuation
                    && string.Equals(_items[i].ExceptionGroupRootEventId, rootEventId, StringComparison.Ordinal))
                    _items.RemoveAt(i);
            }
        }

        NotifyContentChanged();
    }

    public string GetPlainText()
        => string.Join(Environment.NewLine, _items.Select(item => item.ExportLineText));

    internal string GetFullExportPlainText()
    {
        var lines = new List<string>();
        foreach (var group in LogExceptionGroupProjector.BuildGroups(_sourceEvents))
        {
            if (group.HasFoldableContent)
                lines.Add(LogLineDisplayItem.BuildExportText(group, includeContinuations: true));
            else
                lines.Add(LogViewEventFilter.GetDisplayLine(group.RootEvent));
        }

        return string.Join(Environment.NewLine, lines);
    }

    internal string? GetEventIdAt(int index)
        => index >= 0 && index < _items.Count ? _items[index].EventId : null;

    public static ScrollViewer? FindScrollViewer(ListBox listBox)
    {
        if (VisualTreeHelper.GetChildrenCount(listBox) <= 0)
            return null;

        var border = VisualTreeHelper.GetChild(listBox, 0);
        if (VisualTreeHelper.GetChildrenCount(border) <= 0)
            return null;

        return VisualTreeHelper.GetChild(border, 0) as ScrollViewer;
    }

    private void OnListBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox listBox)
            AttachScrollViewer(listBox);
    }

    internal void EnsureScrollViewerResolved()
    {
        if (ScrollViewer is not null || _listBox is null)
            return;

        if (!_listBox.IsLoaded)
        {
            _listBox.ApplyTemplate();
            _listBox.UpdateLayout();
        }

        AttachScrollViewer(_listBox);
    }

    private void AttachScrollViewer(ListBox listBox)
    {
        ScrollViewer = FindScrollViewer(listBox);
        if (ScrollViewer is not null)
            ScrollBarAutoHide.Attach(ScrollViewer);
    }

    private LogViewApplyResult AppendTail(LogViewUpdatePlan plan)
    {
        foreach (var line in plan.AppendedLines)
            _items.Add(LogLineDisplayItem.Parse(line));

        NotifyContentChanged();
        return LogViewApplyResult.Appended;
    }

    internal int FindIndexByEventId(string eventId)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].EventId, eventId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private LogViewApplyResult ReplaceAll(LogViewUpdatePlan plan)
    {
        _sourceEvents.Clear();
        ReplaceItemsWithoutReset(plan.AllLines.Select(LogLineDisplayItem.Parse));
        NotifyContentChanged();
        return LogViewApplyResult.Replaced;
    }

    private IEnumerable<LogLineDisplayItem> ProjectCurrentItems()
        => LogExceptionGroupProjector.Project(_sourceEvents, _exceptionFoldState);

    private void ReplaceItemsWithoutReset(IEnumerable<LogLineDisplayItem> nextItems)
    {
        while (_items.Count > 0)
            _items.RemoveAt(_items.Count - 1);

        foreach (var item in nextItems)
            _items.Add(item);
    }

    private void NotifyContentChanged()
    {
        if (ScrollViewer is not null)
            ScrollBarAutoHide.NotifyContentChanged(ScrollViewer);
    }

    private static DataTemplate CreateItemTemplate()
    {
        var template = new DataTemplate(typeof(LogLineDisplayItem));

        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        factory.SetBinding(
            AutomationProperties.NameProperty,
            new System.Windows.Data.Binding(nameof(LogLineDisplayItem.LineText)));
        factory.SetBinding(
            AutomationProperties.HelpTextProperty,
            new System.Windows.Data.Binding(nameof(LogLineDisplayItem.TagLabel)));

        var tagFactory = new FrameworkElementFactory(typeof(TextBlock));
        tagFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(LogLineDisplayItem.TagLabel)));
        tagFactory.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(LogLineDisplayItem.TagBrush)));
        tagFactory.SetValue(TextBlock.FontStyleProperty, FontStyles.Normal);
        factory.AppendChild(tagFactory);

        var bodyFactory = new FrameworkElementFactory(typeof(TextBlock));
        bodyFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(LogLineDisplayItem.BodyText)));
        bodyFactory.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(LogLineDisplayItem.BodyBrush)));
        bodyFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        factory.AppendChild(bodyFactory);

        template.VisualTree = factory;
        template.Seal();
        return template;
    }

    private static Style CreateFocusVisualStyle()
    {
        var template = new ControlTemplate(typeof(Control));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, SystemColors.HighlightBrush);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        border.SetValue(Border.MarginProperty, new Thickness(1));
        template.VisualTree = border;

        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
}
