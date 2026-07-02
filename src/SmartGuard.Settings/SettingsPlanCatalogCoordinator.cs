using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SettingsPlanCatalogCoordinator : IDisposable
{
  private readonly Window _window;
  private readonly ComboBox _cmbActivePlan;
  private readonly ComboBox _cmbBalancedPlan;
  private readonly ComboBox _cmbPowerSaverPlan;
  private readonly TextBlock? _lblPlanMappingStatus;
  private readonly Func<GuardConfig> _getBaselineConfig;
  private readonly Func<GuardConfig> _readConfigFromUi;
  private CancellationTokenSource? _planCatalogLoadCts;
  private int _planCatalogLoadGeneration;

  internal SettingsPlanCatalogCoordinator(
    Window window,
    ComboBox cmbActivePlan,
    ComboBox cmbBalancedPlan,
    ComboBox cmbPowerSaverPlan,
    TextBlock? lblPlanMappingStatus,
    Func<GuardConfig> getBaselineConfig,
    Func<GuardConfig> readConfigFromUi)
  {
    _window = window;
    _cmbActivePlan = cmbActivePlan;
    _cmbBalancedPlan = cmbBalancedPlan;
    _cmbPowerSaverPlan = cmbPowerSaverPlan;
    _lblPlanMappingStatus = lblPlanMappingStatus;
    _getBaselineConfig = getBaselineConfig;
    _readConfigFromUi = readConfigFromUi;
  }

  internal bool IsUpdatingCombos { get; private set; }

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

  internal void ApplyPlanCombosToUi(GuardConfig config)
  {
    PopulatePlanCombo(_cmbActivePlan, config.ActivePlanGuid, PowerPlanCatalogProvider.HighPerformanceDisplayName);
    PopulatePlanCombo(_cmbBalancedPlan, config.BalancedPlanGuid, PowerPlanCatalogProvider.BalancedDisplayName);
    PopulatePlanCombo(_cmbPowerSaverPlan, config.PowerSaverPlanGuid, PowerPlanCatalogProvider.PowerSaverDisplayName);
    UpdatePlanMappingStatus(config);
  }

  internal void UpdatePlanMappingStatus(GuardConfig? config = null)
  {
    if (_lblPlanMappingStatus is null) return;

    config ??= _readConfigFromUi();
    var messages = PowerPlanMappingValidator.Validate(config, PowerPlanCatalogProvider.TryLoad());
    _lblPlanMappingStatus.Text = messages.Count == 0
      ? "三档计划映射正常"
      : string.Join("；", messages);
  }

  internal static Guid ReadSelectedPlanGuid(ComboBox combo)
  {
    if (combo.SelectedItem is PowerPlanComboItem item)
      return item.PlanGuid;
    if (combo.SelectedValue is Guid guid)
      return guid;
    return Guid.Empty;
  }

  public void Dispose()
  {
    _planCatalogLoadCts?.Cancel();
    _planCatalogLoadCts?.Dispose();
  }

  private void ApplyPlanCatalog(IReadOnlyDictionary<Guid, string> catalog)
  {
    _ = catalog;
    RepopulatePlanCombos();
    UpdatePlanMappingStatus(_getBaselineConfig());
  }

  private void RepopulatePlanCombos()
  {
    var baseline = _getBaselineConfig();
    PopulatePlanCombo(_cmbActivePlan, baseline.ActivePlanGuid, PowerPlanCatalogProvider.HighPerformanceDisplayName);
    PopulatePlanCombo(_cmbBalancedPlan, baseline.BalancedPlanGuid, PowerPlanCatalogProvider.BalancedDisplayName);
    PopulatePlanCombo(_cmbPowerSaverPlan, baseline.PowerSaverPlanGuid, PowerPlanCatalogProvider.PowerSaverDisplayName);
  }

  private void PopulatePlanCombo(ComboBox combo, Guid selectedGuid, string orphanRoleLabel)
  {
    IsUpdatingCombos = true;
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
      IsUpdatingCombos = false;
    }
  }
}
