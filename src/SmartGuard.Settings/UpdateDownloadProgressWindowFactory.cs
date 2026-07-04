using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGuard.Settings;

internal static class UpdateDownloadProgressWindowFactory
{
  internal sealed record DownloadProgressWindow(
    Window Window,
    ProgressBar Bar,
    TextBlock Status,
    CancellationTokenSource Cts);

  internal static DownloadProgressWindow Create(Window owner)
  {
    var cts = new CancellationTokenSource();

    var statusText = new TextBlock
    {
      Text = "正在下载更新...",
      FontSize = 14,
      Foreground = new SolidColorBrush(Colors.Black),
      Margin = new Thickness(0, 0, 0, 12),
    };

    var progressBar = new ProgressBar
    {
      Minimum = 0,
      Maximum = 100,
      Height = 6,
      IsIndeterminate = false,
    };

    var content = new StackPanel();
    content.Children.Add(statusText);
    content.Children.Add(progressBar);

    var surface = new Border
    {
      Background = new SolidColorBrush(Colors.White),
      BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5)),
      BorderThickness = new Thickness(1),
      CornerRadius = new CornerRadius(20),
      Padding = new Thickness(24, 20, 24, 20),
      MinWidth = 320,
      Child = content,
    };

    var window = new Window
    {
      Title = string.Empty,
      WindowStyle = WindowStyle.None,
      AllowsTransparency = true,
      Background = Brushes.Transparent,
      SizeToContent = SizeToContent.WidthAndHeight,
      WindowStartupLocation = WindowStartupLocation.CenterOwner,
      Owner = owner,
      ResizeMode = ResizeMode.NoResize,
      ShowInTaskbar = false,
      Content = surface,
    };

    window.Closing += (_, _) =>
    {
      if (!cts.IsCancellationRequested)
        cts.Cancel();
    };

    return new DownloadProgressWindow(window, progressBar, statusText, cts);
  }
}
