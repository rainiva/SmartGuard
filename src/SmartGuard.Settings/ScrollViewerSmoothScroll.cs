using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartGuard.Settings;

internal static class ScrollViewerSmoothScroll
{
  internal static double WheelImpulseScale { get; set; } = 1.4;
  internal static double FrictionPerSecond { get; set; } = 4.2;
  internal static double MinVelocity { get; set; } = 0.15;
  internal static double FrameIntervalSeconds { get; set; } = 1.0 / 60.0;

  private static readonly DependencyProperty IsAttachedProperty = DependencyProperty.RegisterAttached(
    "IsAttached",
    typeof(bool),
    typeof(ScrollViewerSmoothScroll));

  private static readonly DependencyProperty VelocityYProperty = DependencyProperty.RegisterAttached(
    "VelocityY",
    typeof(double),
    typeof(ScrollViewerSmoothScroll),
    new PropertyMetadata(0.0));

  private static readonly HashSet<ScrollViewer> ActiveScrollViewers = new();
  private static bool _renderHooked;

  internal static void Attach(ScrollViewer scrollViewer)
  {
    if ((bool)scrollViewer.GetValue(IsAttachedProperty))
      return;

    scrollViewer.SetValue(IsAttachedProperty, true);
    scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
    scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
  }

  internal static void SimulateWheelImpulseForTesting(ScrollViewer scrollViewer, int delta)
  {
    var impulse = -delta * WheelImpulseScale;
    if (Math.Abs(impulse) < 0.01)
      return;

    AddVelocity(scrollViewer, impulse);
  }

  internal static void ResetForTesting()
  {
    StopRendering();
    ActiveScrollViewers.Clear();
    WheelImpulseScale = 1.4;
    FrictionPerSecond = 4.2;
    MinVelocity = 0.15;
    FrameIntervalSeconds = 1.0 / 60.0;
  }

  internal static void AdvanceFrameForTesting(ScrollViewer scrollViewer, double elapsedSeconds = -1)
  {
    StepScroll(scrollViewer, elapsedSeconds < 0 ? FrameIntervalSeconds : elapsedSeconds);
  }

  internal static double GetVerticalVelocityForTesting(ScrollViewer scrollViewer)
    => (double)scrollViewer.GetValue(VelocityYProperty);

  private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
  {
    if (sender is not ScrollViewer scrollViewer)
      return;

    // Preview input tunnels through the page before the list's native wheel
    // handler. Leave it unhandled while a nested viewer can consume this
    // direction; at its boundary the page can continue scrolling normally.
    for (var source = e.OriginalSource as DependencyObject;
         source is not null && source != scrollViewer;
         source = GetInputParent(source))
    {
      if (source is ScrollViewer nested &&
          ((e.Delta < 0 && nested.VerticalOffset < nested.ScrollableHeight) ||
           (e.Delta > 0 && nested.VerticalOffset > 0)))
        return;
    }

    ApplyWheelImpulse(scrollViewer, e.Delta);
    e.Handled = true;
  }

  private static DependencyObject? GetInputParent(DependencyObject source)
  {
    if (source is ContentElement content)
      return ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent;
    return source is Visual or System.Windows.Media.Media3D.Visual3D
      ? VisualTreeHelper.GetParent(source)
      : LogicalTreeHelper.GetParent(source);
  }

  private static void ApplyWheelImpulse(ScrollViewer scrollViewer, int delta)
  {
    var impulse = -delta * WheelImpulseScale;
    if (Math.Abs(impulse) < 0.01)
      return;

    AddVelocity(scrollViewer, impulse);
    EnsureRendering();
    ScrollBarAutoHide.NotifyScrollActivity(scrollViewer);
  }

  private static void AddVelocity(ScrollViewer scrollViewer, double impulse)
  {
    var velocity = (double)scrollViewer.GetValue(VelocityYProperty) + impulse;
    scrollViewer.SetValue(VelocityYProperty, velocity);
    ActiveScrollViewers.Add(scrollViewer);
  }

  private static void EnsureRendering()
  {
    if (_renderHooked)
      return;

    CompositionTarget.Rendering += OnRendering;
    _renderHooked = true;
  }

  private static void StopRendering()
  {
    if (!_renderHooked)
      return;

    CompositionTarget.Rendering -= OnRendering;
    _renderHooked = false;
  }

  private static void OnRendering(object? sender, EventArgs e)
  {
    if (ActiveScrollViewers.Count == 0)
    {
      StopRendering();
      return;
    }

    foreach (var scrollViewer in ActiveScrollViewers.ToArray())
      StepScroll(scrollViewer, FrameIntervalSeconds);

    if (ActiveScrollViewers.Count == 0)
      StopRendering();
  }

  private static void StepScroll(ScrollViewer scrollViewer, double elapsedSeconds)
  {
    var velocity = (double)scrollViewer.GetValue(VelocityYProperty);
    if (Math.Abs(velocity) < MinVelocity)
    {
      scrollViewer.SetValue(VelocityYProperty, 0.0);
      ActiveScrollViewers.Remove(scrollViewer);
      return;
    }

    var maxOffset = scrollViewer.ScrollableHeight;
    if (maxOffset <= 0)
    {
      // The ScrollViewer's window has been closed or its content collapsed
      // (e.g. navigating away from the page that hosts it). Detach the
      // inertial animation so the static CompositionTarget.Rendering hook
      // can be released once ActiveScrollViewers becomes empty; otherwise
      // long-running test runs accumulate dead viewers and the rendering
      // callback starves the dispatcher, breaking time-sensitive WPF
      // layout pumps in subsequent tests.
      scrollViewer.SetValue(VelocityYProperty, 0.0);
      ActiveScrollViewers.Remove(scrollViewer);
      return;
    }

    var nextOffset = scrollViewer.VerticalOffset + (velocity * elapsedSeconds);
    nextOffset = Math.Max(0, Math.Min(nextOffset, maxOffset));
    scrollViewer.ScrollToVerticalOffset(nextOffset);

    if (nextOffset <= 0 && velocity < 0)
      velocity = 0;
    else if (nextOffset >= maxOffset && velocity > 0)
      velocity = 0;
    else
      velocity *= Math.Exp(-FrictionPerSecond * elapsedSeconds);

    if (Math.Abs(velocity) < MinVelocity)
    {
      velocity = 0;
      ActiveScrollViewers.Remove(scrollViewer);
    }

    scrollViewer.SetValue(VelocityYProperty, velocity);
  }
}
