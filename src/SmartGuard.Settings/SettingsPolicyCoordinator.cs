using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SettingsPolicyCoordinator
{
  private readonly string _root;
  private readonly GuardConfigRepository _repository;
  private readonly Window _window;
  private readonly ToastNotificationService _toastService;
  private readonly SettingsPlanCatalogCoordinator _planCatalog;
  private readonly SettingsPauseHandler _pauseHandler;
  private readonly SettingsDebouncedSaver _debouncedSaver;
  private readonly NumberBox _sldBalanced;
  private readonly NumberBox _sldSaver;
  private readonly NumberBox _sldBattery;
  private readonly NumberBox _sldPoll;
  private readonly NumberBox _sldBrightMs;
  private readonly NumberBox _sldHeartbeat;
  private readonly ComboBox _cmbActivePlan;
  private readonly ComboBox _cmbBalancedPlan;
  private readonly ComboBox _cmbPowerSaverPlan;
  private readonly CheckBox _tglPaused;
  private readonly CheckBox _tglNotify;
  private readonly CheckBox _tglNotifyExternal;
  private readonly CheckBox _tglAutoStart;

  private GuardConfig _originalConfig;

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
    _tglPaused = tglPaused;
    _tglNotify = tglNotify;
    _tglNotifyExternal = tglNotifyExternal;
    _tglAutoStart = tglAutoStart;
    _planCatalog = new SettingsPlanCatalogCoordinator(
      window,
      cmbActivePlan,
      cmbBalancedPlan,
      cmbPowerSaverPlan,
      lblPlanMappingStatus,
      () => _originalConfig,
      ReadConfigFromUi);
    _pauseHandler = new SettingsPauseHandler(
      root,
      repository,
      tglPaused,
      toastService,
      () => _originalConfig,
      config => _originalConfig = config);
    _debouncedSaver = new SettingsDebouncedSaver(
      window,
      root,
      repository,
      toastService,
      ReadConfigFromUi,
      () => _originalConfig,
      config => _originalConfig = config,
      config => _planCatalog.UpdatePlanMappingStatus(config));
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
    void QueueSave() => _debouncedSaver.QueueSave();
    _sldBalanced.ValueChanged += (_, _) => QueueSave();
    _sldSaver.ValueChanged += (_, _) => QueueSave();
    _sldBattery.ValueChanged += (_, _) => QueueSave();
    _sldPoll.ValueChanged += (_, _) => QueueSave();
    _sldBrightMs.ValueChanged += (_, _) => QueueSave();
    _sldHeartbeat.ValueChanged += (_, _) => QueueSave();

    void QueueSaveAndRefreshPlanStatus()
    {
      _planCatalog.UpdatePlanMappingStatus();
      QueueSave();
    }

    _cmbActivePlan.SelectionChanged += (_, _) =>
    {
      if (_planCatalog.IsUpdatingCombos) return;
      QueueSaveAndRefreshPlanStatus();
    };
    _cmbBalancedPlan.SelectionChanged += (_, _) =>
    {
      if (_planCatalog.IsUpdatingCombos) return;
      QueueSaveAndRefreshPlanStatus();
    };
    _cmbPowerSaverPlan.SelectionChanged += (_, _) =>
    {
      if (_planCatalog.IsUpdatingCombos) return;
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

  internal void OnPauseToggled() => _pauseHandler.OnPauseToggled();

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
      activePlanGuid: SettingsPlanCatalogCoordinator.ReadSelectedPlanGuid(_cmbActivePlan),
      balancedPlanGuid: SettingsPlanCatalogCoordinator.ReadSelectedPlanGuid(_cmbBalancedPlan),
      powerSaverPlanGuid: SettingsPlanCatalogCoordinator.ReadSelectedPlanGuid(_cmbPowerSaverPlan),
      paused: _originalConfig.Paused,
      notifyOnPlanChange: _tglNotify.IsChecked == true,
      notifyOnExternalChange: _tglNotifyExternal.IsChecked == true,
      autoStartEnabled: _tglAutoStart.IsChecked == true);
  }

  internal void CommitSavedConfig(GuardConfig config) => _originalConfig = config;

  internal void SaveThemePreferences(GuardConfig merged)
  {
    SettingsSaveCoordinator.Save(merged, _originalConfig, _root, _repository);
    _originalConfig = merged;
  }

  internal void BeginLoadPlanCatalogAsync() => _planCatalog.BeginLoadPlanCatalogAsync();

  internal void Dispose()
  {
    _debouncedSaver.Dispose();
    _planCatalog.Dispose();
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
    _planCatalog.ApplyPlanCombosToUi(config);
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
