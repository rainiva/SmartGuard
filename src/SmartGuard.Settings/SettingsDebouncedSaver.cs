using System.Windows;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SettingsDebouncedSaver
{
  private readonly Window _window;
  private readonly string _root;
  private readonly GuardConfigRepository _repository;
  private readonly ToastNotificationService _toastService;
  private readonly Func<GuardConfig> _readConfigFromUi;
  private readonly Func<GuardConfig> _getOriginalConfig;
  private readonly Action<GuardConfig> _setOriginalConfig;
  private readonly Action<GuardConfig?> _updatePlanMappingStatus;
  private System.Windows.Threading.DispatcherTimer? _saveDebounceTimer;
  private CancellationTokenSource? _saveCts;

  internal SettingsDebouncedSaver(
    Window window,
    string root,
    GuardConfigRepository repository,
    ToastNotificationService toastService,
    Func<GuardConfig> readConfigFromUi,
    Func<GuardConfig> getOriginalConfig,
    Action<GuardConfig> setOriginalConfig,
    Action<GuardConfig?> updatePlanMappingStatus)
  {
    _window = window;
    _root = root;
    _repository = repository;
    _toastService = toastService;
    _readConfigFromUi = readConfigFromUi;
    _getOriginalConfig = getOriginalConfig;
    _setOriginalConfig = setOriginalConfig;
    _updatePlanMappingStatus = updatePlanMappingStatus;
  }

  internal void QueueSave() => QueueSaveDebounced();

  internal void Dispose()
  {
    _saveDebounceTimer?.Stop();
    _saveCts?.Cancel();
    _saveCts?.Dispose();
  }

  private void QueueSaveDebounced()
  {
    if (_saveDebounceTimer is null)
    {
      _saveDebounceTimer = new System.Windows.Threading.DispatcherTimer(
        System.Windows.Threading.DispatcherPriority.Background,
        _window.Dispatcher)
      {
        Interval = TimeSpan.FromMilliseconds(300)
      };
      _saveDebounceTimer.Tick += (_, _) =>
      {
        _saveDebounceTimer.Stop();
        SaveCurrentSettings();
      };
    }

    _saveDebounceTimer.Stop();
    _saveDebounceTimer.Start();
  }

  private async void SaveCurrentSettings()
  {
    try
    {
      var newConfig = _readConfigFromUi();
      var originalConfig = _getOriginalConfig();

      var errors = GuardConfigValidator.Validate(newConfig);
      errors = errors.Concat(PowerPlanMappingValidator.Validate(newConfig)).ToList();
      if (errors.Count > 0)
      {
        _toastService.Show("保存失败：" + string.Join("；", errors), isError: true);
        return;
      }

      _saveCts?.Cancel();
      _saveCts?.Dispose();
      _saveCts = new CancellationTokenSource();
      var token = _saveCts.Token;

      await Task.Run(() =>
      {
        token.ThrowIfCancellationRequested();
        SettingsSaveCoordinator.Save(newConfig, originalConfig, _root, _repository);
      }, token);
      _setOriginalConfig(newConfig);
      _updatePlanMappingStatus(newConfig);
      _toastService.Show("设置已保存", isError: false);
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
      _toastService.Show($"保存失败：{ex.Message}", isError: true);
    }
  }
}
