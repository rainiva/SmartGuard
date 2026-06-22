using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class PowerPlanCatalogProviderTests
{
    [Fact]
    public async Task LoadAsync_returns_catalog_without_blocking_caller()
    {
        PowerPlanCatalogProvider.ClearSessionCacheForTests();
        var planGuid = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        PowerPlanCatalogProvider.LoadImplementationForTests = () =>
        {
            Thread.Sleep(500);
            return new Dictionary<Guid, string> { [planGuid] = "平衡" };
        };

        try
        {
            var loadTask = PowerPlanCatalogProvider.LoadAsync();
            loadTask.IsCompleted.Should().BeFalse("catalog load should run asynchronously");

            var catalog = await loadTask;
            catalog.Should().ContainKey(planGuid);
            catalog[planGuid].Should().Be("平衡");
        }
        finally
        {
            PowerPlanCatalogProvider.ClearSessionCacheForTests();
        }
    }

  [Fact]
  public void TryLoad_reuses_session_cache_without_calling_loader_again()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      callCount++;
      return new Dictionary<Guid, string>
      {
        [Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")] = "平衡",
      };
    };

    try
    {
      PowerPlanCatalogProvider.TryLoad();
      PowerPlanCatalogProvider.TryLoad();

      callCount.Should().Be(1, "second TryLoad should reuse session cache");
    }
    finally
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }

  [Fact]
  public void TryLoad_retries_after_loader_failure_without_caching_empty_catalog()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    var planGuid = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      callCount++;
      if (callCount == 1)
        throw new TimeoutException("powercfg timed out");

      return new Dictionary<Guid, string> { [planGuid] = "平衡" };
    };

    try
    {
      PowerPlanCatalogProvider.TryLoad().Should().BeEmpty();
      PowerPlanCatalogProvider.TryLoad().Should().ContainKey(planGuid);

      callCount.Should().Be(2);
    }
    finally
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }

  [Fact]
  public void TryLoad_does_not_cache_empty_catalog()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      callCount++;
      return new Dictionary<Guid, string>();
    };

    try
    {
      PowerPlanCatalogProvider.TryLoad().Should().BeEmpty();
      PowerPlanCatalogProvider.TryLoad().Should().BeEmpty();

      callCount.Should().Be(2);
    }
    finally
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }
}
