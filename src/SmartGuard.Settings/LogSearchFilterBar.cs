using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartGuard.Settings;

public sealed class LogSearchFilterBar : Panel
{
    public event EventHandler? FiltersChanged;

    private readonly WrapPanel _chipPanel;
    private readonly TextBox _keywordBox;

    public LogSearchFilterBar()
    {
        _chipPanel = new WrapPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
        };

        _keywordBox = new TextBox
        {
            MinWidth = 80,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _keywordBox.TextChanged += (_, _) => FiltersChanged?.Invoke(this, EventArgs.Empty);

        Children.Add(_chipPanel);
        Children.Add(_keywordBox);
    }

    public string Keyword => _keywordBox.Text;

    public IReadOnlyList<string> ActiveTags =>
        _chipPanel.Children
            .OfType<Border>()
            .Select(chip => chip.Tag as string)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Cast<string>()
            .ToList();

    public void SetKeyword(string keyword) => _keywordBox.Text = keyword ?? string.Empty;

    public void AddTagFilter(string tag)
    {
        var normalized = tag.ToUpperInvariant();
        if (ActiveTags.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        _chipPanel.Children.Add(CreateChip(normalized));
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveTagFilter(string tag)
    {
        var normalized = tag.ToUpperInvariant();
        Border? target = null;
        foreach (var child in _chipPanel.Children.OfType<Border>())
        {
            if (string.Equals(child.Tag as string, normalized, StringComparison.OrdinalIgnoreCase))
            {
                target = child;
                break;
            }
        }

        if (target is null)
            return;

        _chipPanel.Children.Remove(target);
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearTagFilters()
    {
        if (_chipPanel.Children.Count == 0)
            return;

        _chipPanel.Children.Clear();
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _chipPanel.Measure(constraint);
        var remainingWidth = double.IsInfinity(constraint.Width)
            ? double.PositiveInfinity
            : Math.Max(0, constraint.Width - _chipPanel.DesiredSize.Width);
        _keywordBox.Measure(new Size(remainingWidth, constraint.Height));

        var width = Math.Min(
            constraint.Width,
            _chipPanel.DesiredSize.Width + _keywordBox.DesiredSize.Width);
        var height = Math.Max(_chipPanel.DesiredSize.Height, _keywordBox.DesiredSize.Height);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var rowHeight = 0.0;

        _chipPanel.Arrange(new Rect(x, 0, _chipPanel.DesiredSize.Width, finalSize.Height));
        x += _chipPanel.DesiredSize.Width;
        rowHeight = Math.Max(rowHeight, _chipPanel.DesiredSize.Height);

        var keywordWidth = Math.Max(80, finalSize.Width - x);
        _keywordBox.Arrange(new Rect(x, 0, keywordWidth, finalSize.Height));
        rowHeight = Math.Max(rowHeight, _keywordBox.DesiredSize.Height);

        return new Size(finalSize.Width, rowHeight);
    }

    private Border CreateChip(string tag)
    {
        var color = LogTagFilterCatalog.GetTagColor(tag);
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();

        var label = new TextBlock
        {
            Text = tag,
            Foreground = brush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var removeButton = new Button
        {
            Content = "×",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = brush,
            FontSize = 14,
            Padding = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        removeButton.Click += (_, _) => RemoveTagFilter(tag);

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };
        content.Children.Add(label);
        content.Children.Add(removeButton);

        return new Border
        {
            Tag = tag,
            Background = Brushes.Transparent,
            BorderBrush = brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 4, 2),
            Margin = new Thickness(0, 0, 6, 0),
            Child = content,
        };
    }
}

public static class LogTagFilterLinkFactory
{
    public static TextBlock CreateClickableTag(string tag, Action<string> onClick)
    {
        var color = LogTagFilterCatalog.GetTagColor(tag);
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();

        var block = new TextBlock
        {
            Text = tag,
            Foreground = brush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 12, 6),
            Cursor = Cursors.Hand,
            TextDecorations = TextDecorations.Underline,
        };

        block.MouseLeftButtonUp += (_, e) =>
        {
            onClick(tag);
            e.Handled = true;
        };

        return block;
    }
}
