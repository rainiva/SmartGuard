using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SettingsPauseHandler
{
  private readonly string _root;
  private readonly GuardConfigRepository _repository;
  private readonly ConfigMutationService _configMutations;
  private readonly CheckBox _tglPaused;
  private readonly ToastNotificationService _toastService;
  private Func<GuardConfig> _getOriginalConfig;
  private Action<GuardConfig> _setOriginalConfig;

  internal SettingsPauseHandler(
    string root,
    GuardConfigRepository repository,
    CheckBox tglPaused,
    ToastNotificationService toastService,
    Func<GuardConfig> getOriginalConfig,
    Action<GuardConfig> setOriginalConfig)
  {
    _root = root;
    _repository = repository;
    _configMutations = new ConfigMutationService(repository);
    _tglPaused = tglPaused;
    _toastService = toastService;
    _getOriginalConfig = getOriginalConfig;
    _setOriginalConfig = setOriginalConfig;
  }

  internal void OnPauseToggled()
  {
    var paused = _tglPaused.IsChecked == true;
    var originalConfig = _getOriginalConfig();
    if (paused == originalConfig.Paused)
      return;

    try
    {
      _configMutations.SetPaused(paused, _root, SmartGuardPaths.StartupLogFile(_root));
      var loaded = _repository.TryLoad();
      if (loaded is not null)
        _setOriginalConfig(loaded);
    }
    catch (Exception ex)
    {
      _tglPaused.IsChecked = originalConfig.Paused;
      _toastService.Show($"暂停设置失败：{ex.Message}", isError: true);
    }
  }
}
