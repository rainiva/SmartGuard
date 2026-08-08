using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

public class SettingsLogPaginationCoordinatorTests
{
  [Fact]
  public async Task Initial_load_publishes_preview_then_exact_page_one()
  {
    var harness = new Harness();

    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    harness.Flights.Should().ContainSingle();
    harness.Flights[0].Intent.PageNumber.Should().Be(1);
    harness.Flights[0].Intent.CaptureNewSnapshot.Should().BeTrue();

    await harness.CompleteAsync(
      0,
      Model(isProvisional: true, page: 1, pageSize: 20, exactTotalCount: null, isScanComplete: false),
      Model(isProvisional: false, page: 1, pageSize: 20, exactTotalCount: 95, isScanComplete: true));

    harness.Coordinator.State.CurrentPage.Should().Be(1);
    harness.Coordinator.State.ExactTotalCount.Should().Be(95);
    harness.Coordinator.State.IsBusy.Should().BeFalse();
  }

  [Fact]
  public async Task Page_click_uses_refresh_pipeline_and_latest_generation_wins()
  {
    var harness = new Harness();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(0, Model(false, 1, 20, 95, true));

    await harness.Coordinator.GoToPageAsync(5);
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    harness.Flights.Count.Should().Be(3);

    await harness.CompleteAsync(1, Model(false, 5, 20, 95, true));
    harness.Coordinator.State.CurrentPage.Should().Be(1);
    await harness.CompleteAsync(2, Model(false, 1, 20, 95, true));
    harness.Coordinator.State.CurrentPage.Should().Be(1);
  }

  [Fact]
  public async Task Search_change_cancels_index_and_resets_page_one()
  {
    var harness = new Harness();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(0, Model(false, 3, 20, 95, true));

    await harness.Coordinator.ApplyFilterAsync(harness.Expression, harness.Scope, ignoreCase: false);

    harness.Coordinator.State.CurrentPage.Should().Be(1);
    harness.Flights[^1].Intent.CaptureNewSnapshot.Should().BeFalse();
    harness.Flights[^1].Intent.IgnoreCase.Should().BeFalse();
  }

  [Fact]
  public async Task Session_change_uses_same_expression_and_resets_page_one()
  {
    var harness = new Harness();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(0, Model(false, 3, 20, 95, true));

    await harness.Coordinator.ApplySessionAsync(harness.Expression, harness.Scope, ignoreCase: true);

    harness.Flights[^1].Intent.Expression.Should().Be(harness.Expression);
    harness.Flights[^1].Intent.CaptureNewSnapshot.Should().BeFalse();
    harness.Coordinator.State.CurrentPage.Should().Be(1);
  }

  [Fact]
  public async Task Page_size_change_reuses_index_and_resets_page_one()
  {
    var harness = new Harness();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(0, Model(false, 3, 20, 95, true, harness.Index));

    await harness.Coordinator.ChangePageSizeAsync(50);

    harness.Coordinator.State.CurrentPage.Should().Be(1);
    harness.Coordinator.State.PageSize.Should().Be(50);
    harness.Flights[^1].Intent.CaptureNewSnapshot.Should().BeFalse();
    harness.Coordinator.CurrentIndex.Should().BeSameAs(harness.Index);
  }

  [Fact]
  public async Task Filter_time_and_session_changes_reuse_snapshot_but_rebuild_index()
  {
    var harness = new Harness();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(0, Model(false, 3, 20, 95, true, harness.Index));

    await harness.Coordinator.ApplyFilterAsync(harness.Expression, harness.Scope, ignoreCase: true);

    harness.Coordinator.CurrentSnapshot.Should().BeSameAs(harness.Snapshot);
    harness.Coordinator.CurrentIndex.Should().BeNull();
    harness.Flights[^1].Intent.CaptureNewSnapshot.Should().BeFalse();
  }

  [Fact]
  public async Task Sort_and_page_size_changes_reuse_snapshot_and_index()
  {
    var harness = new Harness();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(0, Model(false, 3, 20, 95, true, harness.Index));

    await harness.Coordinator.ToggleSortAsync();
    harness.Coordinator.CurrentIndex.Should().BeSameAs(harness.Index);
    await harness.Coordinator.ChangePageSizeAsync(100);
    harness.Coordinator.CurrentIndex.Should().BeSameAs(harness.Index);
    harness.Flights[^1].Intent.CaptureNewSnapshot.Should().BeFalse();
  }

  [Fact]
  public async Task Only_initial_load_and_explicit_refresh_capture_new_snapshot()
  {
    var harness = new Harness();

    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(0, Model(false, 1, 20, 95, true, harness.Index));
    await harness.Coordinator.ApplyFilterAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.Coordinator.ChangePageSizeAsync(50);
    await harness.Coordinator.ToggleSortAsync();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);

    harness.Flights.Select(flight => flight.Intent.CaptureNewSnapshot)
      .Should().Equal(true, false, false, false, true);
  }

  [Fact]
  public async Task Timer_marks_stale_without_mutating_current_page()
  {
    var harness = new Harness();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(0, Model(false, 3, 20, 95, true, harness.Index));

    harness.Coordinator.MarkSnapshotStale();

    harness.Coordinator.State.CurrentPage.Should().Be(3);
    harness.Coordinator.State.ExactTotalCount.Should().Be(95);
    harness.Coordinator.State.IsStale.Should().BeTrue();
    harness.Flights.Should().ContainSingle();
  }

  [Fact]
  public async Task Refresh_captures_new_snapshot_and_clears_stale()
  {
    var harness = new Harness();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(0, Model(false, 3, 20, 95, true, harness.Index));
    harness.Coordinator.MarkSnapshotStale();

    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    await harness.CompleteAsync(1, Model(false, 1, 20, 95, true, harness.Index));

    harness.Flights[1].Intent.CaptureNewSnapshot.Should().BeTrue();
    harness.Coordinator.State.IsStale.Should().BeFalse();
    harness.Coordinator.State.CurrentPage.Should().Be(1);
  }

  [Fact]
  public async Task Dispose_prevents_late_apply_to_closed_window()
  {
    var harness = new Harness();
    await harness.Coordinator.RefreshAsync(harness.Expression, harness.Scope, ignoreCase: true);
    harness.Coordinator.Dispose();

    await harness.CompleteAsync(0, Model(false, 1, 20, 95, true));

    harness.Coordinator.State.ExactTotalCount.Should().BeNull();
  }

  [Fact]
  public void Pagination_coordinator_does_not_own_a_second_cancellation_source()
  {
    typeof(SettingsLogPaginationCoordinator)
      .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
      .Should()
      .NotContain(field => field.FieldType == typeof(CancellationTokenSource));
  }

  private static LogPaginationApplyModel Model(
    bool isProvisional,
    int page,
    int pageSize,
    int? exactTotalCount,
    bool isScanComplete,
    LogQueryPageIndex? index = null)
    => new(
      QueryGeneration: 0,
      IsProvisional: isProvisional,
      Events: Array.Empty<LogEvent>(),
      PageNumber: page,
      PageSize: pageSize,
      ExactTotalCount: exactTotalCount,
      IsScanComplete: isScanComplete,
      SortDirection: LogSortDirection.NewestFirst,
      Snapshot: index?.Snapshot ?? new LogSourceSnapshot(Array.Empty<LogSourceSnapshotEntry>(), "snapshot"),
      Index: index,
      ErrorMessage: null);

  private sealed class Harness
  {
    internal readonly LogQueryExpression Expression = LogQueryParser.Parse("message~event").Expression!;
    internal readonly LogReadScope Scope = new(Array.Empty<LogSourceIdentity>());
    internal readonly LogSourceSnapshot Snapshot = new(Array.Empty<LogSourceSnapshotEntry>(), "snapshot");
    internal readonly LogQueryPageIndex Index;
    internal readonly SettingsLogPaginationCoordinator Coordinator;
    internal readonly List<ScheduledFlight> Flights = [];
    private IReadOnlyList<LogPaginationApplyModel> _nextModels = Array.Empty<LogPaginationApplyModel>();

    internal Harness()
    {
      Index = new LogQueryPageIndex(Snapshot, Scope, Expression, true, 95, 95, Array.Empty<LogQueryAnchor>());
      Coordinator = new SettingsLogPaginationCoordinator(
        (intent, execute) => Flights.Add(new ScheduledFlight(intent, execute)),
        ExecuteAsync,
        () => false);
    }

    internal async Task CompleteAsync(int flightIndex, params LogPaginationApplyModel[] models)
    {
      _nextModels = models;
      var flight = Flights[flightIndex];
      await flight.Execute(
        flight.Intent,
        new InlineProgress<LogPaginationApplyModel>(Coordinator.ApplyModel),
        CancellationToken.None);
      _nextModels = Array.Empty<LogPaginationApplyModel>();
    }

    private Task ExecuteAsync(
      LogPaginationIntent intent,
      LogSourceSnapshot? snapshot,
      LogQueryPageIndex? index,
      IProgress<LogPaginationApplyModel> progress,
      CancellationToken cancellationToken)
    {
      foreach (var model in _nextModels)
        progress.Report(model with { QueryGeneration = intent.QueryGeneration });
      return Task.CompletedTask;
    }
  }

  private sealed record ScheduledFlight(
    LogPaginationIntent Intent,
    Func<LogPaginationIntent, IProgress<LogPaginationApplyModel>, CancellationToken, Task> Execute);

  private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
  {
    public void Report(T value) => handler(value);
  }
}
