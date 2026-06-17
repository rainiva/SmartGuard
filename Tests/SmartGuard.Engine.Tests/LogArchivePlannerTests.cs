using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class LogArchivePlannerTests
{
  [Fact]
  public void GetArchivePath_appends_yyyyMMdd_bak_suffix()
  {
    LogArchivePlanner.GetArchivePath(@"D:\logs\SmartGuard.log", new DateOnly(2026, 6, 16))
      .Should().Be(@"D:\logs\SmartGuard.log.20260616.bak");
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
  public void TryParseArchiveDate_reads_yyyyMMdd_from_archive_name()
  {
    LogArchivePlanner.TryParseArchiveDate(@"D:\SmartGuard.log.20260616.bak", out var date)
      .Should().BeTrue();
    date.Should().Be(new DateOnly(2026, 6, 16));
  }
}
