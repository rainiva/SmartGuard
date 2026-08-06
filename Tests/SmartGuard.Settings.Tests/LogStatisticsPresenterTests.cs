using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogStatisticsPresenterTests
{
  [Fact]
  public void Apply_empty_result_shows_explicit_empty_state()
  {
    RunOnSta(() =>
    {
      var presenter = CreatePresenter(out var emptyLabel, out var trendChart, out var distribution);

      presenter.Apply(new LogAggregationResult(
        Array.Empty<LogSeverityTimeBucket>(),
        Array.Empty<LogNamedCount>(),
        Array.Empty<LogNamedCount>(),
        Array.Empty<LogNamedCount>(),
        TotalMatchedEvents: 0,
        IsComplete: true,
        IsCancelled: false));

      emptyLabel.Visibility.Should().Be(Visibility.Visible);
      emptyLabel.Text.Should().Contain("无匹配");
      trendChart.Visibility.Should().Be(Visibility.Collapsed);
      distribution.Visibility.Should().Be(Visibility.Collapsed);
    });
  }

  [Fact]
  public void Apply_superseded_result_does_not_render_stale_counts()
  {
    RunOnSta(() =>
    {
      var presenter = CreatePresenter(out var emptyLabel, out var trendChart, out var distribution);

      presenter.Apply(LogAggregationResult.Superseded);

      emptyLabel.Visibility.Should().Be(Visibility.Visible);
      trendChart.Visibility.Should().Be(Visibility.Collapsed);
      distribution.Items.Count.Should().Be(0);
    });
  }

  [Fact]
  public void Apply_matched_result_renders_error_trend_and_component_distribution()
  {
    RunOnSta(() =>
    {
      var presenter = CreatePresenter(out var emptyLabel, out var trendChart, out var distribution);
      var baseTime = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);

      presenter.Apply(new LogAggregationResult(
        [new LogSeverityTimeBucket(baseTime, 2), new LogSeverityTimeBucket(baseTime.AddMinutes(10), 1)],
        [new LogNamedCount(LogComponentRegistry.Engine, 2), new LogNamedCount(LogComponentRegistry.Tray, 1)],
        [new LogNamedCount(LogChannelRegistry.Diagnostics, 3)],
        [new LogNamedCount(EventNameRegistry.IterationError, 2)],
        TotalMatchedEvents: 3,
        IsComplete: true,
        IsCancelled: false));

      emptyLabel.Visibility.Should().Be(Visibility.Collapsed);
      trendChart.Visibility.Should().Be(Visibility.Visible);
      distribution.Visibility.Should().Be(Visibility.Visible);
      distribution.Items.Count.Should().Be(2);
    });
  }

  private static LogStatisticsPresenter CreatePresenter(
    out TextBlock emptyLabel,
    out LogErrorTrendChart trendChart,
    out ItemsControl distribution)
  {
    emptyLabel = new TextBlock();
    trendChart = new LogErrorTrendChart { Width = 240, Height = 72 };
    distribution = new ItemsControl();
    var presenter = new LogStatisticsPresenter();
    presenter.Attach(distribution, trendChart, emptyLabel);
    return presenter;
  }

  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    var thread = new Thread(() =>
    {
      try
      {
        action();
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
      throw failure;
  }
}
