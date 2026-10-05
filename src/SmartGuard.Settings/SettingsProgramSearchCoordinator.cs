using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGuard.Settings;

/// <summary>拥有单窗口搜索、显式刷新、一次在途发现及键盘选择；关闭后取消并丢弃晚到结果。</summary>
internal sealed class SettingsProgramSearchCoordinator : IDisposable
{
  private readonly TextBox _query;
  private readonly ListBox _results;
  private readonly TextBlock _hint;
  private readonly Action<SettingsProgramCandidate> _add;
  private readonly Func<CancellationToken, Task<IReadOnlyList<SettingsProgramCandidate>>> _load;
  private readonly CancellationTokenSource _cancellation = new();
  private IReadOnlyList<SettingsProgramCandidate>? _catalog;
  private bool _loading;
  private bool _disposed;
  private readonly Button? _refresh;
  private readonly Action? _onRefresh;

  internal SettingsProgramSearchCoordinator(TextBox query, ListBox results, TextBlock hint,
    Action<SettingsProgramCandidate> add,
    Func<CancellationToken, Task<IReadOnlyList<SettingsProgramCandidate>>>? load = null,
    Button? refresh = null, Action? onRefresh = null)
  {
    _query = query;
    _results = results;
    _hint = hint;
    _add = add;
    _load = load ?? SettingsProgramCatalog.LoadAsync;
    _refresh = refresh;
    _onRefresh = onRefresh;
    if (_refresh is not null) _refresh.Click += OnRefresh;
    _query.TextChanged += OnQueryChanged;
    _query.GotKeyboardFocus += OnFocus;
    _query.PreviewKeyDown += OnKeyDown;
    _results.PreviewKeyDown += OnKeyDown;
    _results.MouseLeftButtonUp += OnClick;
  }

  private void OnFocus(object sender, KeyboardFocusChangedEventArgs e) => EnsureLoaded();
  private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

  internal void Refresh()
  {
    // A blocked platform call cannot be interrupted: never create a second discovery worker.
    if (_disposed || _loading) return;
    _onRefresh?.Invoke();
    _catalog = null;
    _results.ItemsSource = null;
    _results.Visibility = Visibility.Collapsed;
    EnsureLoaded();
  }
  private void OnQueryChanged(object sender, TextChangedEventArgs e)
  {
    EnsureLoaded();
    UpdateResults();
  }

  private async void EnsureLoaded()
  {
    if (_disposed || _loading || _catalog is not null) return;
    _loading = true;
    if (_refresh is not null) _refresh.IsEnabled = false;
    _hint.Text = "正在读取本机程序…";
    try
    {
      var candidates = await _load(_cancellation.Token);
      if (_disposed) return;
      _catalog = SettingsProgramCatalog.Normalize(candidates);
      UpdateResults();
    }
    catch (OperationCanceledException) { }
    catch (Exception)
    {
      if (!_disposed) _hint.Text = "暂时无法读取程序，请使用浏览文件，或重新聚焦搜索框重试";
    }
    finally
    {
      _loading = false;
      if (!_disposed && _refresh is not null) _refresh.IsEnabled = true;
    }
  }

  private void UpdateResults()
  {
    if (_disposed) return;
    var results = SettingsProgramCatalog.Search(_catalog ?? [], _query.Text);
    _results.ItemsSource = results;
    _results.Visibility = results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    if (results.Count > 0) _results.BringIntoView();
    if (_catalog is null) return;
    _hint.Text = string.IsNullOrWhiteSpace(_query.Text) ? "输入程序名称，例如微信；也可以搜索 exe 文件名"
      : results.Count == 0 ? "未找到程序。请打开实际窗口后刷新，或使用从窗口识别"
      : "选择具体组件添加；快捷方式入口可能不是实际窗口程序";
  }

  private void OnClick(object sender, MouseButtonEventArgs e)
  {
    // Scrollbar clicks must not add a previously selected suggestion.
    if (e.OriginalSource is DependencyObject source
      && ItemsControl.ContainerFromElement(_results, source) is ListBoxItem) AcceptSelected();
  }

  private void OnKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key == Key.Enter) { AcceptSelected(); e.Handled = true; }
    else if (e.Key == Key.Escape) { _query.Clear(); _query.Focus(); e.Handled = true; }
    else if (ReferenceEquals(sender, _query) && e.Key is Key.Down or Key.Up && _results.Items.Count > 0)
    {
      _results.SelectedIndex = Math.Clamp(_results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _results.Items.Count - 1);
      _results.ScrollIntoView(_results.SelectedItem);
      e.Handled = true;
    }
  }

  internal void AcceptSelected()
  {
    if (_disposed || _results.SelectedItem is not SettingsProgramCandidate selected) return;
    _add(selected);
    _query.Clear();
    _query.Focus();
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _cancellation.Cancel();
    _cancellation.Dispose();
    _query.TextChanged -= OnQueryChanged;
    _query.GotKeyboardFocus -= OnFocus;
    _query.PreviewKeyDown -= OnKeyDown;
    _results.PreviewKeyDown -= OnKeyDown;
    _results.MouseLeftButtonUp -= OnClick;
    if (_refresh is not null) _refresh.Click -= OnRefresh;
  }
}
