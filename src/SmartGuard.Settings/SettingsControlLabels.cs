using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal static class SettingsControlLabels
{
  internal static void RegisterNumberBoxLabels(Window window)
  {
    RegisterNumberBoxLabel(
      Require<NumberBox>(window, "sldBalanced"),
      Require<TextBlock>(window, "lblBalanced"),
      "{0} 分钟");
    RegisterNumberBoxLabel(
      Require<NumberBox>(window, "sldSaver"),
      Require<TextBlock>(window, "lblSaver"),
      "{0} 分钟");
    RegisterNumberBoxLabel(
      Require<NumberBox>(window, "sldBattery"),
      Require<TextBlock>(window, "lblBattery"),
      "{0}%");
    RegisterNumberBoxLabel(
      Require<NumberBox>(window, "sldPoll"),
      Require<TextBlock>(window, "lblPoll"),
      "{0} 秒");
    RegisterNumberBoxLabel(
      Require<NumberBox>(window, "sldBrightMs"),
      Require<TextBlock>(window, "lblBrightMs"),
      "{0} 毫秒");
    RegisterHeartbeatLabel(
      Require<NumberBox>(window, "sldHeartbeat"),
      Require<TextBlock>(window, "lblHeartbeat"));
  }

  private static void RegisterNumberBoxLabel(NumberBox numberBox, TextBlock label, string format)
  {
    void Update(object? sender, RoutedPropertyChangedEventArgs<int> e)
      => label.Text = string.Format(format, numberBox.Value);

    numberBox.ValueChanged += Update;
    label.Text = string.Format(format, numberBox.Value);
  }

  private static void RegisterHeartbeatLabel(NumberBox numberBox, TextBlock label)
  {
    void Update(object? sender, RoutedPropertyChangedEventArgs<int> e)
      => label.Text = numberBox.Value == 0 ? "关闭" : $"{numberBox.Value} 分钟";

    numberBox.ValueChanged += Update;
    label.Text = numberBox.Value == 0 ? "关闭" : $"{numberBox.Value} 分钟";
  }

  private static T Require<T>(Window window, string name) where T : class
  {
    return window.FindName(name) as T
      ?? throw new InvalidOperationException($"Missing control: {name}");
  }
}
