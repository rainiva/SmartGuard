using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SettingsPolicyCoordinator
{
  private readonly string _root;
  private readonly GuardConfigRepository _repository;
  private readonly ConfigMutationService _configMutations;
  private readonly Window _window;
  private readonly ToastNotificationService _toastService;
  private readonly NumberBox _sldBalanced;
  private readonly NumberBox _sldSaver;
  private readonly NumberBox _sldBattery;
  private readonly NumberBox _sldPoll;
  private readonly NumberBox _sldBrightMs;
  private readonly NumberBox _sldHeartbeat;
  private readonly ComboBox _cmbActivePlan;
  private readonly ComboBox _cmbBalancedPlan;
  private readonly ComboBox _cmbPowerSaverPlan;
  private readonly TextBlock? _lblPlanMappingStatus;
  private readonly CheckBox _tglPaused;
  private readonly CheckBox _tglNotify;
  private readonly CheckBox _tglNotifyExternal;
  private readonly CheckBox _tglAutoStart;

  private GuardConfig _originalConfig;
  private System.Windows.Threading.DispatcherTimer? _saveDebounceTimer;
  private CancellationTokenSource? _saveCts;
  private CancellationTokenSource? _planCatalogLoadCts;
  private int _planCatalogLoadGeneration;
  private bool _suppressPlanComboEvents;

  internal SettingsPolicyCoordinator(
    string root,
    GuardConfigRepository repository,
    GuardConfig originalConfig,
    Window window,
    ToastNotificationService toastService,
    NumberBox sldBalanced,
    NumberBox sldSaver,
    NumberBox sldBattery,
    NumberBox sldPoll,
    NumberBox sldBrightMs,
    NumberBox sldHeartbeat,
    ComboBox cmbActivePlan,
    ComboBox cmbBalancedPlan,
    ComboBox cmbPowerSaverPlan,
    TextBlock? lblPlanMappingStatus,
    CheckBox tglPaused,
    CheckBox tglNotify,
    CheckBox tglNotifyExternal,
    CheckBox tglAutoStart)
  {
    _root = root;
    _repository = repository;
    _configMutations = new ConfigMutationService(repository);
    _originalConfig = originalConfig;
    _window = window;
    _toastService = toastService;
    _sldBalanced = sldBalanced;
    _sldSaver = sldSaver;
    _sldBattery = sldBattery;
    _sldPoll = sldPoll;
    _sldBrightMs = sldBrightMs;
    _sldHeartbeat = sldHeartbeat;
    _cmbActivePlan = cmbActivePlan;
    _cmbBalancedPlan = cmbBalancedPlan;
    _cmbPowerSaverPlan = cmbPowerSaverPlan;
    _lblPlanMappingStatus = lblPlanMappingStatus;
    _tglPaused = tglPaused;
    _tglNotify = tglNotify;
    _tglNotifyExternal = tglNotifyExternal;
    _tglAutoStart = tglAutoStart;
  }

  internal void ApplyInitialValues(GuardConfig config)
  {
    _sldBalanced.Value = SettingsInitialValues.BalancedThresholdMinutes(config);
    _sldSaver.Value = SettingsInitialValues.PowerSaverThresholdMinutes(config);
    _sldBattery.Value = config.LowBatteryPercent;
    _sldPoll.Value = config.CheckIntervalSec;
    _sldBrightMs.Value = config.BrightnessRestoreMs;
    _sldHeartbeat.Value = config.HeartbeatIntervalMin;
    _tglPaused.IsChecked = config.Paused;
    _tglNotify.IsChecked = config.NotifyOnPlanChange;
    _tglNotifyExternal.IsChecked = config.NotifyOnExternalChange;
    _tglAutoStart.IsChecked = AutoStartService.SyncFromTasks();
  }

  internal void WireInstantApply()
  {
    void QueueSave() => QueueSaveDebounced();
    _sldBalanced.ValueChanged += (_, _) => QueueSave();
    _sldSaver.ValueChanged += (_, _) => QueueSave();
    _sldBattery.ValueChanged += (_, _) => QueueSave();
    _sldPoll.ValueChanged += (_, _) => QueueSave();
    _sldBrightMs.ValueChanged += (_, _) => QueueSave();
    _sldHeartbeat.ValueChanged += (_, _) => QueueSave();

    void QueueSaveAndRefreshPlanStatus()
    {
      UpdatePlanMappingStatus();
      QueueSave();
    }

    _cmbActivePlan.SelectionChanged += (_, _) =>
    {
      if (_suppressPlanComboEvents) return;
      QueueSaveAndRefreshPlanStatus();
    };
    _cmbBalancedPlan.SelectionChanged += (_, _) =>
    {
      if (_suppressPlanComboEvents) return;
      QueueSaveAndRefreshPlanStatus();
    };
    _cmbPowerSaverPlan.SelectionChanged += (_, _) =>
    {
      if (_suppressPlanComboEvents) return;
      QueueSaveAndRefreshPlanStatus();
    };

    _tglPaused.Checked += (_, _) => OnPauseToggled();
    _tglPaused.Unchecked += (_, _) => OnPauseToggled();
    _tglNotify.Checked += (_, _) => QueueSave();
    _tglNotify.Unchecked += (_, _) => QueueSave();
    _tglNotifyExternal.Checked += (_, _) => QueueSave();
    _tglNotifyExternal.Unchecked += (_, _) => QueueSave();
    _tglAutoStart.Checked += (_, _) => QueueSave();
    _tglAutoStart.Unchecked += (_, _) => QueueSave();

    var btnResetDefaults = _window.FindName("btnResetDefaults") as Button;
    if (btnResetDefaults is not null)
      btnResetDefaults.Click += (_, _) => ResetToDefaults();
  }

  internal void OnPauseToggled()
  {
    var paused = _tglPaused.IsChecked == true;
    if (paused == _originalConfig.Paused)
      return;

    try
    {
      _configMutations.SetPaused(paused, _root, SmartGuardPaths.StartupLogFile(_root));
      var loaded = _repository.TryLoad();
      if (loaded is not null)
        _originalConfig = loaded;
    }
    catch (Exception ex)
    {
      _tglPaused.IsChecked = _originalConfig.Paused;
      _toastService.Show($"暂停设置失败：{ex.Message}", isError: true);
    }
  }

  internal GuardConfig ReadConfigFromUi()
  {
    return SettingsSnapshotMapper.ApplyTraySettings(
      _originalConfig,
      balancedThresholdMin: _sldBalanced.Value,
      powerSaverThresholdMin: _sldSaver.Value,
      lowBatteryPercent: _sldBattery.Value,
      checkIntervalSec: _sldPoll.Value,
      brightnessRestoreMs: _sldBrightMs.Value,
      heartbeatIntervalMin: _sldHeartbeat.Value,
      activePlanGuid: ReadSelectedPlanGuid(_cmbActivePlan),
      balancedPlanGuid: ReadSelectedPlanGuid(_cmbBalancedPlan),
      powerSaverPlanGuid: ReadSelectedPlanGuid(_cmbPowerSaverPlan),
      paused: _originalConfig.Paused,
      notifyOnPlanChange: _tglNotify.IsChecked == true,
      notifyOnExternalChange: _tglNotifyExternal.IsChecked == true,
      autoStartEnabled: _tglAutoStart.IsChecked == true);
  }

  internal void CommitSavedConfig(GuardConfig config) => _originalConfig = config;

  internal void BeginLoadPlanCatalogAsync()
  {
    var generation = Interlocked.Increment(ref _planCatalogLoadGeneration);
    _planCatalogLoadCts?.Cancel();
    _planCatalogLoadCts?.Dispose();
    _planCatalogLoadCts = new CancellationTokenSource();

    if (_lblPlanMappingStatus is not null)
      _lblPlanMappingStatus.Text = "正在加载电源计划...";

    _ = Task.Run(() => PowerPlanCatalogProvider.LoadWithRetry())
      .ContinueWith(task =>
      {
        _window.Dispatcher.BeginInvoke(() =>
        {
          if (generation != Volatile.Read(ref _planCatalogLoadGeneration))
            return;

          if (task.IsFaulted)
          {
            if (_lblPlanMappingStatus is not null)
              _lblPlanMappingStatus.Text = "无法读取电源计划，请关闭设置后重试";
            return;
          }

          ApplyPlanCatalog(task.Result);
        });
      }, TaskScheduler.Default);
  }

  internal void Dispose()
  {
    _saveDebounceTimer?.Stop();
    _saveCts?.Cancel();
    _saveCts?.Dispose();
    _planCatalogLoadCts?.Cancel();
    _planCatalogLoadCts?.Dispose();
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
      var newConfig = ReadConfigFromUi();

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
        SettingsSaveCoordinator.Save(newConfig, _originalConfig, _root, _repository);
      }, token);
      _originalConfig = newConfig;
      UpdatePlanMappingStatus(newConfig);
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

  private void ApplyPlanCatalog(IReadOnlyDictionary<Guid, string> catalog)
  {
    _ = catalog;
    RepopulatePlanCombos();
    UpdatePlanMappingStatus(_originalConfig);
  }

  private void RepopulatePlanCombos()
  {
    PopulatePlanCombo(_cmbActivePlan, _originalConfig.ActivePlanGuid, PowerPlanCatalogProvider.HighPerformanceDisplayName);
    PopulatePlanCombo(_cmbBalancedPlan, _originalConfig.BalancedPlanGuid, PowerPlanCatalogProvider.BalancedDisplayName);
    PopulatePlanCombo(_cmbPowerSaverPlan, _originalConfig.PowerSaverPlanGuid, PowerPlanCatalogProvider.PowerSaverDisplayName);
  }

  private void PopulatePlanCombo(ComboBox combo, Guid selectedGuid, string orphanRoleLabel)
  {
    _suppressPlanComboEvents = true;
    try
    {
      combo.DisplayMemberPath = nameof(PowerPlanComboItem.DisplayName);
      combo.SelectedValuePath = nameof(PowerPlanComboItem.PlanGuid);
      var items = PowerPlanComboItemsBuilder.Build(PowerPlanCatalogProvider.TryLoad(), selectedGuid, orphanRoleLabel);
      combo.ItemsSource = items;
      combo.SelectedItem = PlanComboSelection.FindItem(items, selectedGuid);
      if (combo.SelectedItem is null && selectedGuid != Guid.Empty)
        combo.SelectedValue = selectedGuid;
    }
    finally
    {
      _suppressPlanComboEvents = false;
    }
  }

  private static Guid ReadSelectedPlanGuid(ComboBox combo)
  {
    if (combo.SelectedItem is PowerPlanComboItem item)
      return item.PlanGuid;
    if (combo.SelectedValue is Guid guid)
      return guid;
    return Guid.Empty;
  }

  private void UpdatePlanMappingStatus(GuardConfig? config = null)
  {
    if (_lblPlanMappingStatus is null) return;

    config ??= ReadConfigFromUi();
    var messages = PowerPlanMappingValidator.Validate(config, PowerPlanCatalogProvider.TryLoad());
    _lblPlanMappingStatus.Text = messages.Count == 0
      ? "三档计划映射正常"
      : string.Join("；", messages);
  }

  private void ApplyConfigToUi(GuardConfig config)
  {
    _sldBalanced.Value = SettingsInitialValues.BalancedThresholdMinutes(config);
    _sldSaver.Value = SettingsInitialValues.PowerSaverThresholdMinutes(config);
    _sldBattery.Value = config.LowBatteryPercent;
    _sldPoll.Value = config.CheckIntervalSec;
    _sldBrightMs.Value = config.BrightnessRestoreMs;
    _sldHeartbeat.Value = config.HeartbeatIntervalMin;
    _tglPaused.IsChecked = config.Paused;
    _tglNotify.IsChecked = config.NotifyOnPlanChange;
    _tglNotifyExternal.IsChecked = config.NotifyOnExternalChange;
    _tglAutoStart.IsChecked = AutoStartService.SyncFromTasks();
    PopulatePlanCombo(_cmbActivePlan, config.ActivePlanGuid, PowerPlanCatalogProvider.HighPerformanceDisplayName);
    PopulatePlanCombo(_cmbBalancedPlan, config.BalancedPlanGuid, PowerPlanCatalogProvider.BalancedDisplayName);
    PopulatePlanCombo(_cmbPowerSaverPlan, config.PowerSaverPlanGuid, PowerPlanCatalogProvider.PowerSaverDisplayName);
    UpdatePlanMappingStatus(config);
  }

  private void ResetToDefaults()
  {
    if (!AppDialog.ShowConfirm(
          _window,
          "恢复默认策略？",
          "将把守护策略恢复为默认值。\n\n日志文件路径与 GitHub Token 会保留，手动高性能接管会被清除。",
          AppDialogSeverity.Warning))
      return;

    try
    {
      var resetConfig = GuardConfigResetService.CreateResetConfig(_originalConfig, _root);
      SettingsSaveCoordinator.Save(resetConfig, _originalConfig, _root, _repository);
      _originalConfig = resetConfig;
      ApplyConfigToUi(resetConfig);
      _toastService.Show("已恢复默认策略", isError: false);
    }
    catch (Exception ex)
    {
      _toastService.Show($"恢复失败：{ex.Message}", isError: true);
    }
  }
}
