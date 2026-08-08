using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogPaginationStateTests
{
  [Fact]
  public void Zero_results_keeps_disabled_page_one()
  {
    var state = LogPaginationState.Initial.ApplyExactCount(0, isScanComplete: true);

    state.CurrentPage.Should().Be(1);
    state.PageSize.Should().Be(20);
    state.ExactTotalCount.Should().Be(0);
    state.TotalPages.Should().Be(1);
    state.CanGoFirst.Should().BeFalse();
    state.CanGoPrevious.Should().BeFalse();
    state.CanGoNext.Should().BeFalse();
    state.CanGoLast.Should().BeFalse();
    state.CanNavigateToPage(1).Should().BeFalse();
  }

  [Fact]
  public void Page_size_change_resets_to_first_page()
  {
    var state = CreateResolvedState(page: 3, exactTotalCount: 95);

    var changed = state.ChangePageSize(50);

    changed.CurrentPage.Should().Be(1);
    changed.PageSize.Should().Be(50);
    changed.TotalPages.Should().Be(2);
    changed.ExactTotalCount.Should().Be(95);
  }

  [Fact]
  public void Filter_sort_session_and_refresh_reset_to_first_page()
  {
    var state = CreateResolvedState(page: 4, exactTotalCount: 95);

    state.ResetForFilterChange().CurrentPage.Should().Be(1);
    state.ResetForSortChange().CurrentPage.Should().Be(1);
    state.ResetForSessionChange().CurrentPage.Should().Be(1);
    state.ResetForExplicitRefresh().CurrentPage.Should().Be(1);
  }

  [Fact]
  public void Busy_or_inexact_count_disables_numbered_navigation()
  {
    var state = CreateResolvedState(page: 3, exactTotalCount: 95);

    state.CanNavigateToPage(4).Should().BeTrue();
    state.CanGoFirst.Should().BeTrue();
    state.CanGoLast.Should().BeTrue();

    var busy = state.StartNavigation(4);
    var inexact = state.MarkCountInexact(isScanComplete: false);

    busy.IsBusy.Should().BeTrue();
    busy.CanNavigateToPage(2).Should().BeFalse();
    busy.CanGoFirst.Should().BeFalse();
    busy.CanGoLast.Should().BeFalse();

    inexact.ExactTotalCount.Should().BeNull();
    inexact.CanNavigateToPage(2).Should().BeFalse();
    inexact.CanGoFirst.Should().BeFalse();
    inexact.CanGoLast.Should().BeFalse();
  }

  [Fact]
  public void Failed_generation_preserves_last_successful_page()
  {
    var state = CreateResolvedState(page: 3, exactTotalCount: 95);

    var failed = state.StartNavigation(5).FailGeneration();

    failed.CurrentPage.Should().Be(3);
    failed.TotalPages.Should().Be(5);
    failed.IsBusy.Should().BeFalse();
    failed.IsStale.Should().BeTrue();
    failed.CanGoPrevious.Should().BeTrue();
    failed.CanGoNext.Should().BeTrue();
  }

  [Fact]
  public void Failed_generation_after_reset_preserves_last_successful_context()
  {
    var state = CreateResolvedState(page: 3, exactTotalCount: 95);

    var failedAfterPageSizeChange = state.ChangePageSize(50).FailGeneration();
    var failedAfterFilterChange = state.ResetForFilterChange().FailGeneration();

    foreach (var failed in new[] { failedAfterPageSizeChange, failedAfterFilterChange })
    {
      failed.CurrentPage.Should().Be(3);
      failed.PageSize.Should().Be(20);
      failed.ExactTotalCount.Should().Be(95);
      failed.TotalPages.Should().Be(5);
      failed.IsStale.Should().BeTrue();
      failed.CanGoPrevious.Should().BeTrue();
      failed.CanGoNext.Should().BeTrue();
    }
  }

  [Theory]
  [InlineData(10)]
  [InlineData(25)]
  [InlineData(75)]
  public void Page_size_change_rejects_values_outside_reference_options(int pageSize)
  {
    var state = CreateResolvedState(page: 3, exactTotalCount: 95);

    Action change = () => state.ChangePageSize(pageSize);

    change.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void Start_navigation_clamps_requested_page_to_known_bounds()
  {
    var state = LogPaginationState.Initial.ApplyExactCount(95, isScanComplete: true);

    state.StartNavigation(0).CurrentPage.Should().Be(1);
    state.StartNavigation(999).CurrentPage.Should().Be(5);
  }

  private static LogPaginationState CreateResolvedState(int page, int exactTotalCount)
    => LogPaginationState.Initial
      .ApplyExactCount(exactTotalCount, isScanComplete: true)
      .StartNavigation(page)
      .ApplyExactCount(exactTotalCount, isScanComplete: true);
}
