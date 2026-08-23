using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public sealed class SettingsPlanCatalogCoordinatorTests
{
  [Fact]
  public void Dispose_invalidates_catalog_generation_and_blocks_late_ui_apply()
  {
    WpfStaTestHost.Run(() =>
    {
      using var loadEntered = new ManualResetEventSlim();
      using var releaseLoad = new ManualResetEventSlim();
      var loadCompleted = 0;
      var disposed = false;
      SettingsPlanCatalogCoordinator? coordinator = null;

      PowerPlanCatalogProvider.ClearSessionCacheForTests();
      PowerPlanCatalogProvider.LoadImplementationForTests = () =>
      {
        loadEntered.Set();
        if (!releaseLoad.Wait(TimeSpan.FromSeconds(10)))
          throw new TimeoutException("Test did not release the plan catalog load.");

        Interlocked.Exchange(ref loadCompleted, 1);
        return CompleteCatalog();
      };

      try
      {
        var window = new Window();
        var active = new ComboBox();
        var balanced = new ComboBox();
        var saver = new ComboBox();
        var status = new TextBlock();
        var config = GuardConfig.CreateDefault(Path.GetTempPath());

        coordinator = new SettingsPlanCatalogCoordinator(
          window,
          active,
          balanced,
          saver,
          status,
          () => config,
          () => config);

        coordinator.BeginLoadPlanCatalogAsync();
        WpfStaTestHost.WaitUntil(
          () => loadEntered.IsSet,
          TimeSpan.FromSeconds(10),
          "the catalog load to start asynchronously");
        status.Text.Should().Be("正在加载电源计划...");

        coordinator.Dispose();
        disposed = true;
        releaseLoad.Set();

        WpfStaTestHost.WaitUntil(
          () => Volatile.Read(ref loadCompleted) == 1,
          TimeSpan.FromSeconds(10),
          "the canceled catalog load to finish");
        WpfStaTestHost.PumpDispatcher(window);

        active.ItemsSource.Should().BeNull("a disposed coordinator must not apply a late catalog");
        balanced.ItemsSource.Should().BeNull("a disposed coordinator must not apply a late catalog");
        saver.ItemsSource.Should().BeNull("a disposed coordinator must not apply a late catalog");
        status.Text.Should().Be("正在加载电源计划...");
      }
      finally
      {
        releaseLoad.Set();
        if (!disposed)
          coordinator?.Dispose();
        PowerPlanCatalogProvider.ClearSessionCacheForTests();
      }
    });
  }

  [Fact]
  public void Dispose_is_idempotent_after_catalog_load_started()
  {
    WpfStaTestHost.Run(() =>
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
      PowerPlanCatalogProvider.LoadImplementationForTests = CompleteCatalog;
      SettingsPlanCatalogCoordinator? coordinator = null;

      try
      {
        var window = new Window();
        var config = GuardConfig.CreateDefault(Path.GetTempPath());
        coordinator = new SettingsPlanCatalogCoordinator(
          window,
          new ComboBox(),
          new ComboBox(),
          new ComboBox(),
          new TextBlock(),
          () => config,
          () => config);

        coordinator.BeginLoadPlanCatalogAsync();
        coordinator.Dispose();

        Action disposeAgain = coordinator.Dispose;
        disposeAgain.Should().NotThrow("closing a Settings window twice must not fault cleanup");
      }
      finally
      {
        PowerPlanCatalogProvider.ClearSessionCacheForTests();
      }
    });
  }

  private static IReadOnlyDictionary<Guid, string> CompleteCatalog()
    => new Dictionary<Guid, string>
    {
      [PowerPlanCatalogProvider.HighPerformancePlanGuid] = PowerPlanCatalogProvider.HighPerformanceDisplayName,
      [PowerPlanCatalogProvider.BalancedPlanGuid] = PowerPlanCatalogProvider.BalancedDisplayName,
      [PowerPlanCatalogProvider.PowerSaverPlanGuid] = PowerPlanCatalogProvider.PowerSaverDisplayName,
    };
}
