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
  public async Task LoadWithRetryAsync_reuses_session_cache_without_calling_loader_again()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      Interlocked.Increment(ref callCount);
      return new Dictionary<Guid, string>
      {
        [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
        [Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")] = "平衡",
        [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能",
      };
    };

    try
    {
      await PowerPlanCatalogProvider.LoadWithRetryAsync();
      await PowerPlanCatalogProvider.LoadWithRetryAsync();

      callCount.Should().Be(1, "the second load should reuse the session cache");
    }
    finally
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }

  [Fact]
  public async Task LoadWithRetryAsync_retries_after_loader_failure_without_caching_empty_catalog()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      if (Interlocked.Increment(ref callCount) == 1)
        throw new TimeoutException("plan catalog load timed out");

      return new Dictionary<Guid, string>
      {
        [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
        [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
        [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能",
      };
    };

    try
    {
      var catalog = await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 2);

      catalog.Should().ContainKey(PowerPlanCatalogProvider.PowerSaverPlanGuid);
      callCount.Should().Be(2, "the failed first attempt is not cached");
    }
    finally
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }

  [Fact]
  public async Task LoadWithRetryAsync_does_not_cache_empty_catalog()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      Interlocked.Increment(ref callCount);
      return new Dictionary<Guid, string>();
    };

    try
    {
      (await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1)).Should().BeEmpty();
      (await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1)).Should().BeEmpty();

      callCount.Should().Be(4, "each call makes one attempt plus the final reload, and nothing is cached");
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
  public async Task LoadWithRetryAsync_does_not_cache_incomplete_catalog()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      Interlocked.Increment(ref callCount);
      return new Dictionary<Guid, string>
      {
        [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
        [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
      };
    };

    try
    {
      (await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1)).Should().ContainKey(PowerPlanCatalogProvider.BalancedPlanGuid);
      await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);

      callCount.Should().Be(4, "an incomplete catalog is never cached");
      PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Loaded);
      PowerPlanCatalogProvider.Snapshot.PresenceOf(PowerPlanCatalogProvider.PowerSaverPlanGuid)
        .Should().Be(PowerPlanPresence.ConfirmedAbsent);
    }
    finally
    {
      PowerPlanCatalogProvider.ClearSessionCacheForTests();
    }
  }

  [Fact]
  public async Task LoadWithRetryAsync_returns_complete_catalog_from_loader()
  {
    PowerPlanCatalogProvider.ClearSessionCacheForTests();
    var callCount = 0;
    PowerPlanCatalogProvider.LoadImplementationForTests = () =>
    {
      if (Interlocked.Increment(ref callCount) == 1)
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
      var catalog = await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 2);
      catalog.Should().ContainKey(PowerPlanCatalogProvider.PowerSaverPlanGuid);
      callCount.Should().Be(2);
    }
    finally
    {
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

    [Fact]
    public async Task TryLoadOnceAsync_reads_listed_and_hidden_known_names_through_the_scheme_service()
    {
        var custom = Guid.Parse("56c8d9b8-d34d-4d19-96a4-c177cf5f4882");
        var names = new Dictionary<Guid, string>
        {
            [PowerPlanCatalogProvider.BalancedPlanGuid] = "平衡",
            [custom] = "自定义模式",
            [PowerPlanCatalogProvider.HighPerformancePlanGuid] = "高性能",
            [PowerPlanCatalogProvider.PowerSaverPlanGuid] = "节能",
        };
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid, custom]),
            ReadName = guid => names.TryGetValue(guid, out var name) ? (0u, name) : (2u, string.Empty),
        };
        var statuses = new List<PowerSchemeStatus>();
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string>();
        await PowerSchemeTestScope.WithNative(native, async () =>
            catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add));
        catalog.Should().BeEquivalentTo(names);
        statuses.Should().OnlyContain(status => status.Succeeded);
        native.Calls.Should().Contain("enumerate");
    }

    [Fact]
    public async Task TryLoadOnceAsync_reports_enumeration_failure_and_returns_empty()
    {
        var native = new FakePowrProfNative { Enumerate = () => (5, []) };
        var statuses = new List<PowerSchemeStatus>();
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string> { [Guid.Empty] = "x" };
        await PowerSchemeTestScope.WithNative(native, async () =>
            catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add));
        catalog.Should().BeEmpty();
        statuses.Should().ContainSingle().Which.Should().Be(new PowerSchemeStatus("api-error", 5, statuses[0].ElapsedMilliseconds));
    }

    [Fact]
    public async Task Missing_hidden_known_scheme_is_not_reported_as_a_failure()
    {
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid]),
            ReadName = guid => guid == PowerPlanCatalogProvider.PowerSaverPlanGuid ? (2u, string.Empty) : (0u, "n"),
        };
        var statuses = new List<PowerSchemeStatus>();
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string>();
        await PowerSchemeTestScope.WithNative(native, async () =>
            catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add));
        catalog.Should().NotContainKey(PowerPlanCatalogProvider.PowerSaverPlanGuid);
        statuses.Should().OnlyContain(status => status.Succeeded);
    }

    [Fact]
    public async Task Hidden_known_scheme_that_cannot_be_read_is_reported_as_a_failure()
    {
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid]),
            ReadName = guid => guid == PowerPlanCatalogProvider.PowerSaverPlanGuid ? (5u, string.Empty) : (0u, "n"),
        };
        var statuses = new List<PowerSchemeStatus>();
        await PowerSchemeTestScope.WithNative(native, async () =>
            await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add));
        statuses.Should().Contain(status => status.FailureKind == "api-error" && status.Win32Error == 5,
            "only a confirmed absence (Win32 2) is normal for a hidden scheme");
    }

    [Fact]
    public async Task Name_reads_share_one_total_budget_and_return_a_partial_catalog()
    {
        var listed = Enumerable.Range(0, 8).Select(i => new Guid($"00000000-0000-0000-0000-{i:D12}")).ToArray();
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, listed),
            // Each read is far below its own 2 s deadline; only their sum exceeds the total budget.
            ReadName = guid => { Thread.Sleep(100); return (0, guid.ToString("N")); },
        };
        var statuses = new List<PowerSchemeStatus>();
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string>();
        var elapsed = TimeSpan.Zero;
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            catalog = await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, statuses.Add,
                budget: TimeSpan.FromMilliseconds(350));
            elapsed = watch.Elapsed;
            await PowerSchemeTestScope.WaitUntilIdleAsync();
            // A partial catalog is never cached: a second load enumerates again.
            await PowerPlanCatalogProvider.TryLoadOnceAsync(CancellationToken.None, budget: TimeSpan.FromMilliseconds(350));
        });
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "the chain is bounded by the total budget, not by the scheme count");
        // Only the upper bound is asserted: how many 100 ms reads finish within the budget depends on the test pool.
        catalog.Count.Should().BeLessThan(listed.Length);
        statuses.Should().Contain(status => status.FailureKind == "timed-out");
        native.Calls.Count(call => call == "enumerate").Should().Be(2);
    }

    private static readonly Guid Custom = Guid.Parse("56c8d9b8-d34d-4d19-96a4-c177cf5f4882");

    [Fact]
    public async Task LoadWithRetryAsync_reads_names_through_the_scheme_service()
    {
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid, Custom,
                PowerPlanCatalogProvider.HighPerformancePlanGuid, PowerPlanCatalogProvider.PowerSaverPlanGuid]),
            ReadName = guid => (0, guid == Custom ? "自定义模式" : guid.ToString("N")),
        };
        IReadOnlyDictionary<Guid, string> catalog = new Dictionary<Guid, string>();
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Loading);
            catalog = await PowerPlanCatalogProvider.LoadWithRetryAsync();
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Loaded);
            PowerPlanCatalogProvider.Snapshot.PresenceOf(Custom).Should().Be(PowerPlanPresence.Named);
        });
        catalog.Should().ContainKey(Custom).WhoseValue.Should().Be("自定义模式");
        native.Calls.Count(call => call == "enumerate").Should().Be(1, "a complete catalog needs no retry");
    }

    [Fact]
    public async Task Enumeration_failing_on_every_attempt_publishes_a_failed_snapshot()
    {
        var native = new FakePowrProfNative { Enumerate = () => (5, []) };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            (await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1)).Should().BeEmpty();
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Failed,
                "an exhausted load is not 'still loading'");
            PowerPlanCatalogProvider.Snapshot.PresenceOf(Custom).Should().Be(PowerPlanPresence.ListUnavailable);
        });
        native.Calls.Count(call => call == "enumerate").Should().Be(2, "one attempt plus the existing final reload");
    }

    [Fact]
    public async Task A_later_failure_keeps_the_last_loaded_snapshot_and_a_later_success_recovers()
    {
        var failing = false;
        var native = new FakePowrProfNative
        {
            Enumerate = () => failing ? (5u, []) : (0u, [PowerPlanCatalogProvider.BalancedPlanGuid, Custom]),
            // Power saver is genuinely absent, so the catalog stays incomplete and is never cached.
            ReadName = guid => guid == PowerPlanCatalogProvider.PowerSaverPlanGuid ? (2u, string.Empty) : (0u, "n"),
        };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            var loaded = PowerPlanCatalogProvider.Snapshot;
            loaded.State.Should().Be(PowerPlanCatalogState.Loaded);

            failing = true;
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            PowerPlanCatalogProvider.Snapshot.Should().BeSameAs(loaded, "a failed reload must not erase a real result");

            failing = false;
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Loaded);
            PowerPlanCatalogProvider.Snapshot.Should().NotBeSameAs(loaded);
        });
    }

    [Fact]
    public async Task Failed_then_successful_load_moves_from_failed_to_loaded()
    {
        var failing = true;
        var native = new FakePowrProfNative
        {
            Enumerate = () => failing ? (5u, []) : (0u, [PowerPlanCatalogProvider.BalancedPlanGuid]),
        };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Failed);
            failing = false;
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            PowerPlanCatalogProvider.Snapshot.State.Should().Be(PowerPlanCatalogState.Loaded);
        });
    }

    [Fact]
    public async Task Presence_separates_unreadable_names_from_confirmed_absence()
    {
        var unknownCustom = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var native = new FakePowrProfNative
        {
            Enumerate = () => (0, [PowerPlanCatalogProvider.BalancedPlanGuid, Custom]),
            ReadName = guid =>
                guid == Custom ? (5u, string.Empty)                                     // listed, name unreadable
                : guid == PowerPlanCatalogProvider.PowerSaverPlanGuid ? (2u, string.Empty) // hidden, confirmed absent
                : guid == PowerPlanCatalogProvider.HighPerformancePlanGuid ? (5u, string.Empty) // hidden, unknown
                : (0u, "平衡"),
        };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            await PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1);
            var snapshot = PowerPlanCatalogProvider.Snapshot;
            snapshot.PresenceOf(PowerPlanCatalogProvider.BalancedPlanGuid).Should().Be(PowerPlanPresence.Named);
            snapshot.PresenceOf(Custom).Should().Be(PowerPlanPresence.NameUnreadable, "it exists; only its name failed");
            snapshot.PresenceOf(PowerPlanCatalogProvider.PowerSaverPlanGuid).Should().Be(PowerPlanPresence.ConfirmedAbsent);
            snapshot.PresenceOf(PowerPlanCatalogProvider.HighPerformancePlanGuid).Should().Be(PowerPlanPresence.NameUnreadable,
                "a hidden built-in scheme whose read failed is unknown, not absent");
            snapshot.PresenceOf(unknownCustom).Should().Be(PowerPlanPresence.ConfirmedAbsent,
                "a non-built-in scheme that enumeration did not list does not exist");
        });
    }

    [Fact]
    public async Task LoadWithRetryAsync_propagates_cancellation_through_the_scheme_service()
    {
        using var release = new ManualResetEventSlim();
        var native = new FakePowrProfNative { Enumerate = () => { release.Wait(TimeSpan.FromSeconds(10)); return (0, []); } };
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    PowerPlanCatalogProvider.LoadWithRetryAsync(cancellationToken: cancellation.Token));
            }
            finally { release.Set(); }
        });
        native.Calls.Should().Contain("enumerate");
    }

    [Fact]
    public async Task LoadWithRetryAsync_serializes_concurrent_loads_that_cannot_be_cached()
    {
        // High performance and power saver are genuinely absent, so no load is cached and each caller
        // loads maxAttempts + 1 = 2 times. Cache reuse is covered by LoadWithRetryAsync_reuses_completed_catalog….
        var native = new FakePowrProfNative
        {
            Enumerate = () => { Thread.Sleep(50); return (0, [PowerPlanCatalogProvider.BalancedPlanGuid]); },
            ReadName = guid => guid == PowerPlanCatalogProvider.BalancedPlanGuid ? (0u, "平衡") : (2u, string.Empty),
        };
        IReadOnlyDictionary<Guid, string>[] results = [];
        await PowerSchemeTestScope.WithNative(native, async () =>
        {
            results = await Task.WhenAll(
                PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1),
                PowerPlanCatalogProvider.LoadWithRetryAsync(maxAttempts: 1));
        });
        // Without the load gate the second caller would meet the executor's single-flight gate, get
        // call-pending without reaching native code, and the count would fall below 4.
        native.Calls.Count(call => call == "enumerate").Should().Be(4);
        results.Should().AllSatisfy(catalog => catalog.Should().ContainKey(PowerPlanCatalogProvider.BalancedPlanGuid));
    }
}
