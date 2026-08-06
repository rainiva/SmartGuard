using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class LogStatisticsPresenter
{
  private ItemsControl? _componentDistribution;
  private LogErrorTrendChart? _errorTrendChart;
  private TextBlock? _emptyStateLabel;

  internal void Attach(
    ItemsControl? componentDistribution,
    LogErrorTrendChart? errorTrendChart,
    TextBlock? emptyStateLabel)
  {
    _componentDistribution = componentDistribution;
    _errorTrendChart = errorTrendChart;
    _emptyStateLabel = emptyStateLabel;
  }

  internal void Apply(LogAggregationResult? result)
  {
    if (_componentDistribution is null || _errorTrendChart is null || _emptyStateLabel is null)
      return;

    if (result is null || result.IsCancelled || result.IsSuperseded)
    {
      ShowEmptyState("统计已取消或过期。");
      return;
    }

    if (result.TotalMatchedEvents == 0)
    {
      ShowEmptyState("当前查询无匹配事件。");
      return;
    }

    _emptyStateLabel.Visibility = Visibility.Collapsed;
    _errorTrendChart.Visibility = Visibility.Visible;
    _componentDistribution.Visibility = Visibility.Visible;
    _errorTrendChart.SetBuckets(result.SeverityBuckets);
    _componentDistribution.ItemsSource = result.ComponentCounts
      .Select(item => new LogComponentDistributionItem(item.Name, item.Count))
      .ToList();
  }

  private void ShowEmptyState(string message)
  {
    _emptyStateLabel!.Text = message;
    _emptyStateLabel.Visibility = Visibility.Visible;
    _errorTrendChart!.Visibility = Visibility.Collapsed;
    _componentDistribution!.Visibility = Visibility.Collapsed;
    _errorTrendChart.SetBuckets(Array.Empty<LogSeverityTimeBucket>());
    _componentDistribution.ItemsSource = Array.Empty<LogComponentDistributionItem>();
  }

  internal sealed record LogComponentDistributionItem(string Name, int Count);
}

public sealed class LogErrorTrendChart : FrameworkElement
{
  private DrawingVisual? _visual;
  private IReadOnlyList<LogSeverityTimeBucket> _buckets = Array.Empty<LogSeverityTimeBucket>();

  internal void SetBuckets(IReadOnlyList<LogSeverityTimeBucket> buckets)
  {
    _buckets = buckets;
    RenderChart();
  }

  protected override int VisualChildrenCount => _visual is null ? 0 : 1;

  protected override Visual GetVisualChild(int index)
    => _visual ?? throw new ArgumentOutOfRangeException(nameof(index));

  protected override Size MeasureOverride(Size availableSize)
    => new(Math.Max(120, availableSize.Width), Math.Max(64, availableSize.Height));

  protected override Size ArrangeOverride(Size finalSize)
  {
    RenderChart();
    return finalSize;
  }

  private void RenderChart()
  {
    RemoveVisualChild(_visual);
    _visual = new DrawingVisual();

    var width = ActualWidth > 0 ? ActualWidth : 240;
    var height = ActualHeight > 0 ? ActualHeight : 64;
    if (_buckets.Count == 0)
    {
      AddVisualChild(_visual);
      return;
    }

    var maxCount = Math.Max(1, _buckets.Max(bucket => bucket.ErrorCount));
    var barWidth = width / _buckets.Count;
    using var context = _visual.RenderOpen();
    var baseline = height - 4;

    for (var index = 0; index < _buckets.Count; index++)
    {
      var bucket = _buckets[index];
      var barHeight = bucket.ErrorCount <= 0
        ? 0
        : Math.Max(2, bucket.ErrorCount / (double)maxCount * (height - 12));
      var x = index * barWidth + 1;
      var rect = new Rect(x, baseline - barHeight, Math.Max(1, barWidth - 2), barHeight);
      context.DrawRectangle(
        new SolidColorBrush(Color.FromRgb(220, 76, 64)),
        null,
        rect);
    }

    AddVisualChild(_visual);
  }
}
