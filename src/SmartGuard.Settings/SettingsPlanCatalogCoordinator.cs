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
  private readonly object _planCatalogLoadLock = new();
  private CancellationTokenSource? _planCatalogLoadCts;
  private int _planCatalogLoadGeneration;
  private int _disposed;

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
    var loadCts = new CancellationTokenSource();
    CancellationTokenSource? previousCts;
    int generation;
    lock (_planCatalogLoadLock)
    {
      if (Volatile.Read(ref _disposed) != 0)
      {
        loadCts.Dispose();
        return;
      }

      generation = Interlocked.Increment(ref _planCatalogLoadGeneration);
      previousCts = _planCatalogLoadCts;
      _planCatalogLoadCts = loadCts;
    }

    previousCts?.Cancel();
    previousCts?.Dispose();

    if (_lblPlanMappingStatus is not null)
      _lblPlanMappingStatus.Text = "正在加载电源计划...";

    _ = LoadAndApplyPlanCatalogAsync(generation, loadCts.Token);
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
    var snapshot = PowerPlanCatalogProvider.Snapshot;
    if (snapshot.State == PowerPlanCatalogState.Loading)
    {
      _lblPlanMappingStatus.Text = "正在加载电源计划...";
      return;
    }
    if (snapshot.State == PowerPlanCatalogState.Failed)
    {
      _lblPlanMappingStatus.Text = "无法读取电源计划列表，可重新打开设置重试";
      return;
    }
    var messages = PowerPlanMappingValidator.Validate(config, snapshot);
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
    CancellationTokenSource? cts;
    lock (_planCatalogLoadLock)
    {
      if (Volatile.Read(ref _disposed) != 0)
        return;

      Volatile.Write(ref _disposed, 1);
      Interlocked.Increment(ref _planCatalogLoadGeneration);
      cts = _planCatalogLoadCts;
      _planCatalogLoadCts = null;
    }

    cts?.Cancel();
    cts?.Dispose();
  }

  private void ApplyPlanCatalog(IReadOnlyDictionary<Guid, string> catalog)
  {
    _ = catalog;
    RepopulatePlanCombos();
    UpdatePlanMappingStatus(_getBaselineConfig());
  }

  private async Task LoadAndApplyPlanCatalogAsync(
    int generation,
    CancellationToken cancellationToken)
  {
    try
    {
      var catalog = await PowerPlanCatalogProvider.LoadWithRetryAsync(
          cancellationToken: cancellationToken)
        .ConfigureAwait(false);

      if (!CanApply(generation, cancellationToken))
        return;

      await _window.Dispatcher.InvokeAsync(() =>
      {
        if (CanApply(generation, cancellationToken))
          ApplyPlanCatalog(catalog);
      });
    }
    catch (OperationCanceledException)
      when (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
    {
      // Cancellation is the normal close/supersede path.
    }
    catch (Exception)
    {
      if (!CanApply(generation, cancellationToken))
        return;

      try
      {
        await _window.Dispatcher.InvokeAsync(() =>
        {
          if (_lblPlanMappingStatus is not null && CanApply(generation, cancellationToken))
            _lblPlanMappingStatus.Text = "无法读取电源计划，请关闭设置后重试";
        });
      }
      catch (InvalidOperationException) when (_window.Dispatcher.HasShutdownStarted)
      {
        // The window is closing; the failure cannot be presented.
      }
    }
  }

  private bool CanApply(int generation, CancellationToken cancellationToken)
    => !cancellationToken.IsCancellationRequested
       && Volatile.Read(ref _disposed) == 0
       && generation == Volatile.Read(ref _planCatalogLoadGeneration)
       && !_window.Dispatcher.HasShutdownStarted
       && !_window.Dispatcher.HasShutdownFinished;

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
      var items = PowerPlanComboItemsBuilder.Build(PowerPlanCatalogProvider.Snapshot, selectedGuid, orphanRoleLabel);
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
