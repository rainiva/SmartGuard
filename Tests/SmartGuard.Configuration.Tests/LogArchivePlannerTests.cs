using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogArchivePlannerTests
{
  [Fact]
  public void AllocateArchivePath_starts_at_sequence_001_for_empty_directory()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var logPath = Path.Combine(dir, "SmartGuard.log");
    try
    {
      LogArchivePlanner.AllocateArchivePath(logPath, new DateOnly(2026, 6, 16))
        .Should().Be(Path.Combine(dir, "SmartGuard.log.20260616.001.bak"));
    }
    finally
    {
      if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
  }

  [Theory]
  [InlineData("2026-06-15", "2026-06-16", true)]
  [InlineData("2026-06-16", "2026-06-16", false)]
  [InlineData("2026-06-17", "2026-06-16", false)]
  public void ShouldRotateForCalendarDay_when_file_day_is_before_today(
    string fileDay, string today, bool expected)
  {
    LogArchivePlanner.ShouldRotateForCalendarDay(
      DateOnly.Parse(fileDay),
      DateOnly.Parse(today)).Should().Be(expected);
  }

  [Theory]
  [InlineData("2026-06-09", "2026-06-16", true)]
  [InlineData("2026-06-10", "2026-06-16", false)]
  [InlineData("2026-06-16", "2026-06-16", false)]
  public void IsArchiveExpired_deletes_older_than_seven_days(
    string archiveDay, string today, bool expectedExpired)
  {
    LogArchivePlanner.IsArchiveExpired(
      DateOnly.Parse(archiveDay),
      DateOnly.Parse(today),
      retainDays: 7).Should().Be(expectedExpired);
  }

  [Fact]
  public void TryParseArchiveDate_reads_legacy_yyyyMMdd_bak()
  {
    LogArchivePlanner.TryParseArchiveDate(@"D:\SmartGuard.log.20260616.bak", out var date)
      .Should().BeTrue();
    date.Should().Be(new DateOnly(2026, 6, 16));
  }

  [Fact]
  public void TryParseArchiveIdentity_reads_sequenced_archive_name()
  {
    LogArchivePlanner.TryParseArchiveIdentity(@"D:\SmartGuard.log.20260616.003.bak", out var date, out var sequence)
      .Should().BeTrue();
    date.Should().Be(new DateOnly(2026, 6, 16));
    sequence.Should().Be(3);
  }

  [Fact]
  public void SelectArchivesBeyondCount_picks_oldest_over_limit()
  {
    var archives = new (string Path, DateOnly Date)[]
    {
      (@"C:\logs\a.log.20260730.bak", new DateOnly(2026, 7, 30)),
      (@"C:\logs\a.log.20260728.bak", new DateOnly(2026, 7, 28)),
      (@"C:\logs\a.log.20260729.bak", new DateOnly(2026, 7, 29)),
    };

    var doomed = LogArchivePlanner.SelectArchivesBeyondCount(archives, maxArchiveFiles: 2);

    doomed.Should().Equal(@"C:\logs\a.log.20260728.bak");
  }

  [Fact]
  public void SelectArchivesBeyondCount_returns_empty_within_limit_or_disabled()
  {
    var archives = new (string Path, DateOnly Date)[]
    {
      (@"C:\logs\a.log.20260730.bak", new DateOnly(2026, 7, 30)),
    };

    LogArchivePlanner.SelectArchivesBeyondCount(archives, maxArchiveFiles: 1).Should().BeEmpty();
    LogArchivePlanner.SelectArchivesBeyondCount(archives, maxArchiveFiles: 0)
      .Should().BeEmpty("0 表示禁用数量上限");
  }

  [Fact]
  public void SelectArchivesOverBudget_deletes_oldest_until_under_budget()
  {
    var archives = new (string Path, DateOnly Date, long SizeBytes)[]
    {
      (@"C:\logs\a.log.20260729.bak", new DateOnly(2026, 7, 29), 400),
      (@"C:\logs\a.log.20260728.bak", new DateOnly(2026, 7, 28), 400),
      (@"C:\logs\a.log.20260730.bak", new DateOnly(2026, 7, 30), 400),
    };

    var doomed = LogArchivePlanner.SelectArchivesOverBudget(
      archives, currentLogSizeBytes: 100, maxDirectoryBytes: 1_000);

    doomed.Should().Equal(@"C:\logs\a.log.20260728.bak");
  }

  [Fact]
  public void SelectArchivesOverBudget_never_selects_current_log_even_when_it_alone_exceeds_budget()
  {
    var archives = new (string Path, DateOnly Date, long SizeBytes)[]
    {
      (@"C:\logs\a.log.20260729.bak", new DateOnly(2026, 7, 29), 400),
    };

    var doomed = LogArchivePlanner.SelectArchivesOverBudget(
      archives, currentLogSizeBytes: 5_000, maxDirectoryBytes: 1_000);

    doomed.Should().Equal(new[] { @"C:\logs\a.log.20260729.bak" },
      "只能删归档；当前日志即使独自超预算也绝不入选");
  }

  [Fact]
  public void SelectArchivesOverBudget_returns_empty_when_disabled_or_within_budget()
  {
    var archives = new (string Path, DateOnly Date, long SizeBytes)[]
    {
      (@"C:\logs\a.log.20260730.bak", new DateOnly(2026, 7, 30), 400),
    };

    LogArchivePlanner.SelectArchivesOverBudget(archives, 100, maxDirectoryBytes: 0)
      .Should().BeEmpty("0 表示禁用容量上限");
    LogArchivePlanner.SelectArchivesOverBudget(archives, 100, maxDirectoryBytes: 1_000)
      .Should().BeEmpty();
  }
}
