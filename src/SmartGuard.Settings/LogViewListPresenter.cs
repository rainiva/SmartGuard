using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

    public ScrollViewer? ScrollViewer { get; private set; }

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

    public string GetPlainText()
        => string.Join(Environment.NewLine, _items.Select(item => item.LineText));

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

    private LogViewApplyResult ReplaceAll(LogViewUpdatePlan plan)
    {
        _items.Clear();
        foreach (var line in plan.AllLines)
            _items.Add(LogLineDisplayItem.Parse(line));

        NotifyContentChanged();
        return LogViewApplyResult.Replaced;
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
