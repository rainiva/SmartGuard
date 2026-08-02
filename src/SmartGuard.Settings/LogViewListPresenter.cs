using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            ReplaceItemsWithoutReset(delta.InitializeEvents.Select(LogLineDisplayItem.FromEvent));
            NotifyContentChanged();
            return LogViewApplyResult.Replaced;
        }

        ApplyRemovals(delta.RemovedHeadCount, delta.RemovedTailCount);

        if (delta.PrependedEvents.Count > 0)
        {
            for (var i = delta.PrependedEvents.Count - 1; i >= 0; i--)
                _items.Insert(0, LogLineDisplayItem.FromEvent(delta.PrependedEvents[i]));
        }

        foreach (var logEvent in delta.AppendedEvents)
            _items.Add(LogLineDisplayItem.FromEvent(logEvent));

        NotifyContentChanged();

        return delta.AppendedEvents.Count > 0 || delta.PrependedEvents.Count > 0
            ? LogViewApplyResult.Appended
            : LogViewApplyResult.Replaced;
    }

    public string GetPlainText()
        => string.Join(Environment.NewLine, _items.Select(item => item.LineText));

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
        {
            ScrollBarAutoHide.Attach(ScrollViewer);
        }
    }

    private LogViewApplyResult AppendTail(LogViewUpdatePlan plan)
    {
        foreach (var line in plan.AppendedLines)
            _items.Add(LogLineDisplayItem.Parse(line));

        NotifyContentChanged();
        return LogViewApplyResult.Appended;
    }

    private LogViewApplyResult ReplaceAll(LogViewUpdatePlan plan)
    {
        ReplaceItemsWithoutReset(plan.AllLines.Select(LogLineDisplayItem.Parse));
        NotifyContentChanged();
        return LogViewApplyResult.Replaced;
    }

    private void ReplaceItemsWithoutReset(IEnumerable<LogLineDisplayItem> nextItems)
    {
        while (_items.Count > 0)
            _items.RemoveAt(_items.Count - 1);

        foreach (var item in nextItems)
            _items.Add(item);
    }

    private void ApplyRemovals(int removedHeadCount, int removedTailCount)
    {
        for (var i = 0; i < removedHeadCount && _items.Count > 0; i++)
            _items.RemoveAt(0);

        for (var i = 0; i < removedTailCount && _items.Count > 0; i++)
            _items.RemoveAt(_items.Count - 1);
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
}
