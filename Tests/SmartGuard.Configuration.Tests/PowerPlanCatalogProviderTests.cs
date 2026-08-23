using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("PowerPlanCatalogProvider")]
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
        [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
        [Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")] = "平衡",
        [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能",
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

  [Fact]
  public void HasMissingKnownSchemes_is_true_when_hidden_power_saver_is_absent()
  {
    var catalog = new Dictionary<Guid, string>
    {
      [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
      [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
    };

    PowerPlanCatalogProvider.HasMissingKnownSchemes(catalog).Should().BeTrue();
  }

  [Fact]
  public void HasMissingKnownSchemes_is_false_when_all_default_schemes_present()
  {
    var catalog = new Dictionary<Guid, string>
    {
      [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
      [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
      [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能",
    };

    PowerPlanCatalogProvider.HasMissingKnownSchemes(catalog).Should().BeFalse();
  }

  [Fact]
  public void TryLoad_does_not_cache_incomplete_catalog()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      callCount++;
      if (callCount == 1)
      {
        return new Dictionary<Guid, string>
        {
          [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
        };
      }

      return new Dictionary<Guid, string>
      {
        [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
        [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
        [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能",
      };
    };

    try
    {
      PowerPlanCatalogProvider.TryLoad().Should().ContainKey(PowerPlanCatalogProvider.BalancedPlanGuid);
      PowerPlanCatalogProvider.TryLoad().Should().ContainKey(PowerPlanCatalogProvider.PowerSaverPlanGuid);
      callCount.Should().Be(2);
    }
    finally
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }

  [Fact]
  public void LoadWithRetry_returns_complete_catalog_from_loader()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      callCount++;
      if (callCount == 1)
      {
        return new Dictionary<Guid, string>
        {
          [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
        };
      }

      return new Dictionary<Guid, string>
      {
        [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
        [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
        [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能",
      };
    };

    try
    {
      var catalog = PowerPlanCatalogProvider.LoadWithRetry(maxAttempts: 2);
      catalog.Should().ContainKey(PowerPlanCatalogProvider.PowerSaverPlanGuid);
      callCount.Should().Be(2);
    }
    finally
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }

  [Fact]
  public async Task LoadWithRetryAsync_propagates_cancellation_to_powercfg()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    using var cancellation = new CancellationTokenSource();
    using var started = new ManualResetEventSlim();
    PowerCfgProcessRunner.RunAsyncOverrideForTests = async (_, _, token) =>
    {
      started.Set();
      await Task.Delay(Timeout.InfiniteTimeSpan, token);
      return new PowerCfgCommandResult(true, false, 0, string.Empty, string.Empty);
    };

    try
    {
      var loadTask = PowerPlanCatalogProvider.LoadWithRetryAsync(
        maxAttempts: 1,
        cancellationToken: cancellation.Token);

      started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the async powercfg call should start");
      cancellation.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await loadTask);
    }
    finally
    {
      PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }

  [Fact]
  public async Task LoadWithRetryAsync_serializes_concurrent_powercfg_loaders()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var active = 0;
    var maximumActive = 0;
    PowerCfgProcessRunner.RunAsyncOverrideForTests = async (_, _, token) =>
    {
      var current = Interlocked.Increment(ref active);
      var observedMaximum = Volatile.Read(ref maximumActive);
      while (current > observedMaximum)
      {
        var replaced = Interlocked.CompareExchange(
          ref maximumActive,
          current,
          observedMaximum);
        if (replaced == observedMaximum)
          break;
        observedMaximum = replaced;
      }

      try
      {
        await Task.Delay(100, token);
        return new PowerCfgCommandResult(true, false, 0, string.Empty, string.Empty);
      }
      finally
      {
        Interlocked.Decrement(ref active);
      }
    };

    try
    {
      await Task.WhenAll(
        PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1),
        PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1));

      maximumActive.Should().Be(1, "concurrent Settings windows must share one catalog load slot");
    }
    finally
    {
      PowerCfgProcessRunner.RunAsyncOverrideForTests = null;
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }

  [Fact]
  public async Task LoadWithRetryAsync_reuses_completed_catalog_for_concurrent_callers()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var calls = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      Interlocked.Increment(ref calls);
      Thread.Sleep(100);
      return new Dictionary<Guid, string>
      {
        [PowerPlanCatalogProvider.HighPerformancePlanGuid] = PowerPlanCatalogProvider.HighPerformanceDisplayName,
        [PowerPlanCatalogProvider.BalancedPlanGuid] = PowerPlanCatalogProvider.BalancedDisplayName,
        [PowerPlanCatalogProvider.PowerSaverPlanGuid] = PowerPlanCatalogProvider.PowerSaverDisplayName,
      };
    };

    try
    {
      await Task.WhenAll(
        PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1),
        PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1));

      calls.Should().Be(1, "a completed catalog is shared by concurrent Settings windows");
    }
    finally
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }
}
