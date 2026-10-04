using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView.Tests;

public class LogTailReaderTests
{
  [Fact]
  public void ReadFromOffset_reads_text_after_offset()
  {
    var path = WriteTemp("tail.log", "alpha\nbeta\n");

    var slice = LogTailReader.ReadFromOffset(path, 6);

    slice.Length.Should().BeGreaterThan(0);
    slice.Text.Should().Be("beta\n");
  }

  [Fact]
  public void ReadFromOffset_returns_empty_when_file_missing()
  {
    var slice = LogTailReader.ReadFromOffset(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log"), 0);
    slice.Length.Should().Be(0);
    slice.Text.Should().BeEmpty();
  }

  [Fact]
  public void GetFileLength_returns_byte_length_without_reading_content()
  {
    var path = WriteTemp("length.log", "alpha\nbeta\n");

    LogTailReader.GetFileLength(path).Should().Be(new FileInfo(path).Length);
  }

  [Fact]
  public void GetFileLength_returns_zero_when_file_missing()
  {
    LogTailReader.GetFileLength(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log")).Should().Be(0);
  }

  [Fact]
  public void ReadRecentTail_reads_only_end_of_large_file()
  {
    var path = WriteTemp("large.log", "HEAD_START\n" + new string('A', 500_000) + "TAIL_MARKER\n");

    var slice = LogTailReader.ReadRecentTail(path, maxBytes: 4096);

    slice.Length.Should().BeGreaterThan(500_000);
    slice.Text.Should().Contain("TAIL_MARKER");
    slice.Text.Should().NotContain("HEAD_START");
  }

  [Fact]
  public void ReadInitialView_uses_recent_tail_instead_of_full_primary_file()
  {
    var primary = WriteTemp("primary.log", "HEAD_START\n" + new string('B', 120_000) + "[INFO] 2026-06-16 18:00:00 tail\n");
    var fallback = WriteTemp("fallback.log", "2026-06-16 16:00:00 startup\n");

    var initial = LogTailReader.ReadInitialView(primary, fallback, maxTailBytes: 4096);

    initial.FileLength.Should().BeGreaterThan(120_000);
    initial.Text.Should().Contain("18:00:00");
    initial.Text.Should().NotContain("HEAD_START");
    initial.Text.Should().Contain("16:00:00");
  }

  [Fact]
  public void ReadInitialView_includes_retained_archives_in_chronological_order()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var primary = Path.Combine(root, "SmartGuard.log");
    var olderArchive = primary + ".20260730.bak";
    var newerArchive = primary + ".20260731.bak";
    try
    {
      File.WriteAllText(olderArchive, "[INFO] 2026-07-30 09:00:00 archive older\n");
      File.WriteAllText(newerArchive, "[INFO] 2026-07-31 09:00:00 archive newer\n");
      File.WriteAllText(primary, "[INFO] 2026-08-01 09:00:00 current\n");

      var initial = LogTailReader.ReadInitialView(primary, fallbackLogPath: null);

      initial.Text.Should().Contain("archive older");
      initial.Text.Should().Contain("archive newer");
      initial.Text.IndexOf("archive older", StringComparison.Ordinal).Should()
        .BeLessThan(initial.Text.IndexOf("archive newer", StringComparison.Ordinal));
      initial.Text.IndexOf("archive newer", StringComparison.Ordinal).Should()
        .BeLessThan(initial.Text.IndexOf("current", StringComparison.Ordinal));
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch { }
    }
  }

  [Fact]
  public void ReadInitialView_reports_truncation_when_merged_sources_exceed_budget()
  {
    var primary = WriteTemp("primary-budget.log", "primary-" + new string('p', 55) + "\n");
    var fallback = WriteTemp("fallback-budget.log", "fallback-" + new string('f', 55) + "\n");

    var initial = LogTailReader.ReadInitialView(primary, fallback, maxTailBytes: 100);

    initial.IsTailTruncated.Should().BeTrue();
  }

  [Fact]
  public void ReadFullWithFallback_merges_primary_and_fallback_chronologically()
  {
    var primary = WriteTemp("primary.log", "[INFO] 2026-06-16 17:00:00 main log");
    var fallback = WriteTemp("fallback.log", "2026-06-16 16:00:00 startup log");

    var text = LogTailReader.ReadFullWithFallback(primary, fallback);

    text.Should().Contain("16:00:00");
    text.Should().Contain("17:00:00");
    text!.IndexOf("16:00:00", StringComparison.Ordinal).Should()
      .BeLessThan(text.IndexOf("17:00:00", StringComparison.Ordinal));
    text.Should().NotContain("--- fallback ---");
  }

  private static string WriteTemp(string name, string content)
  {
    var path = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"), name);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    return path;
  }

  private static string LegacyLine(int hour, int minute, int second, string message)
    => $"[INFO] 2026-08-01 {hour:D2}:{minute:D2}:{second:D2} [Engine] {message}";

  private static LogReadRequest DefaultRequest(LogReadCursor? cursor = null, LogReadDirection direction = LogReadDirection.Latest)
    => new(cursor, direction, LogTailReader.DefaultTargetEventCount, LogTailReader.DefaultMaxBytes);

  [Fact]
  public async Task ReadLatest_returns_last_200_newest_events_with_has_older()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var lines = Enumerable.Range(1, 250)
      .Select(i => LegacyLine(i / 3600, (i / 60) % 60, i % 60, $"Event{i}"))
      .ToArray();
    await File.WriteAllLinesAsync(logPath, lines);

    try
    {
      var reader = new LogTailReader(logPath);
      var page = await reader.ReadLatestAsync(DefaultRequest(), CancellationToken.None);

      page.Events.Should().HaveCount(200);
      page.Events.Last().Message.Should().Contain("Event250");
      page.HasOlder.Should().BeTrue();
      page.OlderCursor.Should().NotBeNull();
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public async Task ReadOlder_pages_are_contiguous_without_duplicates_across_bak()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var archive = logPath + ".20260730.bak";
    await File.WriteAllLinesAsync(archive, Enumerable.Range(1, 120)
      .Select(i => LegacyLine(8, 0, i, $"Archive{i}")));
    await File.WriteAllLinesAsync(logPath, Enumerable.Range(121, 120)
      .Select(i => LegacyLine(9, 0, i - 120, $"Current{i}")));

    try
    {
      var reader = new LogTailReader(logPath);
      var latest = await reader.ReadLatestAsync(DefaultRequest(), CancellationToken.None);
      var older = await reader.ReadOlderAsync(
        DefaultRequest(latest.OlderCursor, LogReadDirection.Older),
        CancellationToken.None);

      var ids = latest.Events.Select(e => e.EventId)
        .Concat(older.Events.Select(e => e.EventId))
        .ToList();
      ids.Should().OnlyHaveUniqueItems();

      var combined = latest.Events.Concat(older.Events).OrderBy(e => e.Timestamp).ToList();
      for (var i = 1; i < combined.Count; i++)
        combined[i].Timestamp.Should().BeOnOrAfter(combined[i - 1].Timestamp);
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public async Task ReadNewer_returns_only_complete_events_after_cursor()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    await File.WriteAllTextAsync(logPath, LegacyLine(10, 0, 0, "First") + "\n");

    try
    {
      var reader = new LogTailReader(logPath);
      var initial = await reader.ReadLatestAsync(DefaultRequest(), CancellationToken.None);

      await File.AppendAllTextAsync(logPath, LegacyLine(10, 0, 1, "Second") + "\n");
      await File.AppendAllTextAsync(logPath, "[INFO] 2026-08-01 10:00:02 [Engine] HalfLine");

      var newer = await reader.ReadNewerAsync(
        DefaultRequest(initial.NewerCursor, LogReadDirection.Newer),
        CancellationToken.None);

      newer.Events.Should().ContainSingle(e => e.Message.Contains("Second"));
      newer.Events.Should().NotContain(e => e.Message.Contains("HalfLine"));
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public async Task ReadNewer_completes_half_line_on_next_read()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    await File.WriteAllTextAsync(logPath, LegacyLine(10, 0, 0, "First") + "\n");

    try
    {
      var reader = new LogTailReader(logPath);
      var initial = await reader.ReadLatestAsync(DefaultRequest(), CancellationToken.None);

      await File.AppendAllTextAsync(logPath, "[INFO] 2026-08-01 10:00:01 [Engine] Half");
      var partial = await reader.ReadNewerAsync(
        DefaultRequest(initial.NewerCursor, LogReadDirection.Newer),
        CancellationToken.None);
      partial.Events.Should().BeEmpty();

      await File.AppendAllTextAsync(logPath, "Line\n");
      var completed = await reader.ReadNewerAsync(
        DefaultRequest(initial.NewerCursor, LogReadDirection.Newer),
        CancellationToken.None);
      completed.Events.Should().ContainSingle(e => e.Message.Contains("HalfLine"));
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public async Task Rotate_and_truncate_continue_with_event_id_deduplication()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    await File.WriteAllLinesAsync(logPath, [
      LegacyLine(10, 0, 0, "BeforeRotate"),
      LegacyLine(10, 0, 1, "BeforeRotate2"),
    ]);

    try
    {
      var reader = new LogTailReader(logPath);
      var before = await reader.ReadLatestAsync(DefaultRequest(), CancellationToken.None);
      var sharedId = before.Events.Single(e => e.Message.Contains("BeforeRotate2")).EventId;

      var archive = logPath + ".20260801.bak";
      File.Move(logPath, archive);
      await File.WriteAllLinesAsync(logPath, [
        LegacyLine(10, 0, 2, "AfterRotate"),
      ]);

      var readerAfter = new LogTailReader(logPath);
      var after = await readerAfter.ReadLatestAsync(DefaultRequest(), CancellationToken.None);
      after.Events.Select(e => e.EventId).Should().Contain(sharedId);
      after.Events.Select(e => e.EventId).Should().OnlyHaveUniqueItems();
      after.Events.Last().Message.Should().Contain("AfterRotate");
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public async Task Identical_legacy_lines_are_not_wrongly_deduped()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var duplicate = LegacyLine(10, 0, 0, "DuplicateMessage");
    await File.WriteAllLinesAsync(logPath, [duplicate, duplicate]);

    try
    {
      var reader = new LogTailReader(logPath);
      var page = await reader.ReadLatestAsync(
        new LogReadRequest(null, LogReadDirection.Latest, 10, LogTailReader.DefaultMaxBytes),
        CancellationToken.None);

      page.Events.Should().HaveCount(2);
      page.Events.Select(e => e.EventId).Should().OnlyHaveUniqueItems();
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public async Task Archive_rename_preserves_legacy_event_id()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var line = LegacyLine(10, 0, 0, "PersistId");
    await File.WriteAllTextAsync(logPath, line + "\n");

    try
    {
      var reader = new LogTailReader(logPath);
      var before = await reader.ReadLatestAsync(DefaultRequest(), CancellationToken.None);
      var idBefore = before.Events.Single().EventId;

      var archive = logPath + ".20260801.bak";
      File.Move(logPath, archive);
      await File.WriteAllTextAsync(logPath, string.Empty);

      var readerAfter = new LogTailReader(logPath);
      var after = await readerAfter.ReadLatestAsync(DefaultRequest(), CancellationToken.None);
      after.Events.Should().ContainSingle(e => e.EventId == idBefore);
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public async Task Startup_fallback_uses_the_visible_chronological_page_boundaries()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var startup = Path.Combine(root, "startup.log");
    await File.WriteAllTextAsync(startup, "2026-08-01 08:00:00 startup-only\n");
    await File.WriteAllTextAsync(logPath, LegacyLine(10, 0, 0, "MainOnly") + "\n");

    try
    {
      var reader = new LogTailReader(logPath, startup);
      var page = await reader.ReadLatestAsync(DefaultRequest(), CancellationToken.None);

      page.Events.Should().HaveCount(2);
      page.Events.First().Message.Should().Contain("startup-only");
      page.OlderCursor!.Source.Should().Be(LogSourceIdentity.FromFilePath(startup));
      page.NewerCursor!.Source.Should().NotBe(LogSourceIdentity.FromFilePath(startup));
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public async Task Cancelled_read_returns_empty_page_without_partial_results()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    await File.WriteAllLinesAsync(logPath, Enumerable.Range(1, 50)
      .Select(i => LegacyLine(10, 0, i, $"Event{i}")));

    try
    {
      var reader = new LogTailReader(logPath);
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var page = await reader.ReadLatestAsync(DefaultRequest(), cts.Token);
      page.Events.Should().BeEmpty();
      page.OlderCursor.Should().BeNull();
      page.NewerCursor.Should().BeNull();
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  private static string CreateRoot()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
  }

  private static void CleanupRoot(string root)
  {
    try { Directory.Delete(root, recursive: true); } catch { }
  }
}
