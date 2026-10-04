using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView.Tests;

public sealed class StartupFallbackHistoryTests : IDisposable
{
  [Fact]
  public async Task Merged_history_can_return_to_physical_end_after_reading_to_start()
  {
    var main = Path.Combine(_root, "main.log"); var startup = Path.Combine(_root, "startup.log");
    File.WriteAllLines(main, Enumerable.Range(0, 400).Select(i => $"[INFO] 2026-10-04 10:00:00 main-{i:D4}"));
    File.WriteAllLines(startup, Enumerable.Range(0, 400).Select(i => $"[INFO] 2026-10-04 10:00:00 fallback-{i:D4}"));
    var reader = new LogTailReader(main, startup);
    var request = new LogReadRequest(null, LogReadDirection.Latest, 20, 8192);
    var page = await reader.ReadLatestAsync(request, CancellationToken.None);
    var oldest = page;
    for (var i = 0; i < 100 && page.Events.Count > 0; i++)
    {
      oldest = page;
      page = await reader.ReadOlderAsync(request with { Cursor = page.OlderCursor, Direction = LogReadDirection.Older }, CancellationToken.None);
    }
    page.Events.Should().BeEmpty("the first pass must actually reach the physical start");
    var ids = oldest.Events.Select(e => e.EventId).ToList();
    page = oldest;
    for (var i = 0; i < 100 && page.Events.Count > 0; i++)
    {
      page = await reader.ReadNewerAsync(request with { Cursor = page.NewerCursor, Direction = LogReadDirection.Newer }, CancellationToken.None);
      ids.AddRange(page.Events.Select(e => e.EventId));
    }
    page.Events.Should().BeEmpty("the reverse pass must reach the physical end");
    ids.Should().HaveCount(800).And.OnlyHaveUniqueItems();
  }

  [Fact]
  public async Task Partial_primary_batch_repeated_in_fallback_cannot_skip_unique_fallback_rows()
  {
    var main = Path.Combine(_root, "main.log"); var startup = Path.Combine(_root, "startup.log");
    var events = Enumerable.Range(0, 400).Select(_ => Event(1)).ToArray();
    File.WriteAllLines(main, events.Take(200).Select(LogJsonlFormatter.FormatLine));
    File.WriteAllLines(startup, events.Select(LogJsonlFormatter.FormatLine));
    var reader = new LogTailReader(main, startup);
    var request = new LogReadRequest(null, LogReadDirection.Latest, 20, 262144);
    var page = await reader.ReadLatestAsync(request, CancellationToken.None);
    var ids = new HashSet<string>();
    for (var i = 0; i < 100 && page.Events.Count > 0; i++)
    {
      page.Events.Select(e => e.EventId).Should().OnlyHaveUniqueItems();
      foreach (var item in page.Events) ids.Add(item.EventId);
      page = await reader.ReadOlderAsync(request with { Cursor = page.OlderCursor, Direction = LogReadDirection.Older }, CancellationToken.None);
    }
    ids.Count.Should().Be(400);
  }

  [Fact]
  public async Task Merged_equal_timestamp_history_preserves_all_physical_rows()
  {
    var main = Path.Combine(_root, "main.log"); var startup = Path.Combine(_root, "startup.log");
    File.WriteAllLines(main, Enumerable.Range(0, 400).Select(i => $"[INFO] 2026-10-04 10:00:00 main-{i:D4}"));
    File.WriteAllLines(startup, Enumerable.Range(0, 400).Select(i => $"[INFO] 2026-10-04 10:00:00 fallback-{i:D4}"));
    var reader = new LogTailReader(main, startup);
    var request = new LogReadRequest(null, LogReadDirection.Latest, 20, 8192);
    var page = await reader.ReadLatestAsync(request, CancellationToken.None);
    var ids = new List<string>();
    for (var i = 0; i < 100 && page.Events.Count > 0; i++)
    {
      ids.AddRange(page.Events.Select(e => e.EventId));
      page = await reader.ReadOlderAsync(request with { Cursor = page.OlderCursor, Direction = LogReadDirection.Older }, CancellationToken.None);
    }
    ids.Should().HaveCount(800).And.OnlyHaveUniqueItems();
  }

  [Fact]
  public async Task Main_newer_page_reports_physical_end_when_timestamp_order_differs()
  {
    var main = Path.Combine(_root, "main.log");
    File.WriteAllLines(main, [LogJsonlFormatter.FormatLine(Event(10))]);
    var reader = new LogTailReader(main);
    var request = new LogReadRequest(null, LogReadDirection.Latest, 20, 262144);
    var latest = await reader.ReadLatestAsync(request, CancellationToken.None);
    File.AppendAllLines(main, [LogJsonlFormatter.FormatLine(Event(9)), LogJsonlFormatter.FormatLine(Event(8))]);
    var next = await reader.ReadNewerAsync(request with { Cursor = latest.NewerCursor, Direction = LogReadDirection.Newer }, CancellationToken.None);
    next.Events.Should().HaveCount(2);
    next.HasNewer.Should().BeFalse("both appended physical lines were returned through the end of the file");
  }

  [Fact]
  public async Task Newly_created_fallback_admits_old_timestamps_without_replaying_main()
  {
    var main = Path.Combine(_root, "main.log"); var startup = Path.Combine(_root, "startup.log");
    File.WriteAllLines(main, [LogJsonlFormatter.FormatLine(Event(10))]);
    var reader = new LogTailReader(main, startup);
    var request = new LogReadRequest(null, LogReadDirection.Latest, 20, 262144);
    var page = await reader.ReadLatestAsync(request, CancellationToken.None);
    var appended = Event(1);
    File.WriteAllLines(startup, [LogJsonlFormatter.FormatLine(appended)]);
    var next = await reader.ReadNewerAsync(request with { Cursor = page.NewerCursor, Direction = LogReadDirection.Newer }, CancellationToken.None);
    next.Events.Should().ContainSingle(e => e.EventId == appended.EventId);
  }

  [Fact]
  public async Task Main_only_equal_timestamps_page_to_physical_start_without_skipping_lines()
  {
    var main = Path.Combine(_root, "main.log");
    File.WriteAllLines(main, Enumerable.Range(0, 4000).Select(i => $"[INFO] 2026-08-01 10:00:00 entry-{i:D4} padding"));
    var reader = new LogTailReader(main, Path.Combine(_root, "missing-startup.log"));
    var request = new LogReadRequest(null, LogReadDirection.Latest, 200, 262144);
    var page = await reader.ReadLatestAsync(request, CancellationToken.None);
    var lineBytes = System.Text.Encoding.UTF8.GetByteCount("[INFO] 2026-08-01 10:00:00 entry-0000 padding" + Environment.NewLine);
    page.OlderCursor!.ByteOffset.Should().Be(3800L * lineBytes);
    var ids = new HashSet<string>();
    for (var i = 0; i < 100 && page.Events.Count > 0; i++)
    {
      foreach (var item in page.Events) ids.Add(item.EventId);
      page = await reader.ReadOlderAsync(request with { Cursor = page.OlderCursor, Direction = LogReadDirection.Older }, CancellationToken.None);
    }
    ids.Count.Should().Be(4000);
  }

  [Fact]
  public async Task New_physical_lines_with_older_timestamps_are_not_lost()
  {
    var main = Path.Combine(_root, "main.log"); var startup = Path.Combine(_root, "startup.log");
    File.WriteAllLines(main, [LogJsonlFormatter.FormatLine(Event(10))]);
    File.WriteAllLines(startup, [LogJsonlFormatter.FormatLine(Event(9))]);
    var reader = new LogTailReader(main, startup);
    var request = new LogReadRequest(null, LogReadDirection.Latest, 20, 262144);
    var latest = await reader.ReadLatestAsync(request, CancellationToken.None);
    var appended = Event(1);
    File.AppendAllLines(startup, [LogJsonlFormatter.FormatLine(appended)]);
    var next = await reader.ReadNewerAsync(request with { Cursor = latest.NewerCursor, Direction = LogReadDirection.Newer }, CancellationToken.None);
    next.Events.Should().ContainSingle(e => e.EventId == appended.EventId);
  }

  private readonly string _root = Path.Combine(Path.GetTempPath(), "SmartGuardStartup_" + Guid.NewGuid().ToString("N"));
  public StartupFallbackHistoryTests() => Directory.CreateDirectory(_root);

  [Fact]
  public async Task Newer_fallback_and_three_archives_are_visible_and_deduplicated_by_event_id()
  {
    var main = Path.Combine(_root, "main.log"); var startup = Path.Combine(_root, "startup.log");
    var old = Event(0); var newest = Event(5);
    File.WriteAllLines(main, [LogJsonlFormatter.FormatLine(old)]);
    File.WriteAllLines(startup, [LogJsonlFormatter.FormatLine(old), LogJsonlFormatter.FormatLine(newest)]);
    for (var i = 1; i <= 4; i++) File.WriteAllLines(startup + $".2026100{i}.001.bak", [LogJsonlFormatter.FormatLine(Event(i))]);
    var reader = new LogTailReader(main, startup);
    var request = new LogReadRequest(null, LogReadDirection.Latest, 20, 262144);
    var page = await reader.ReadLatestAsync(request, CancellationToken.None);
    page.Events.Should().HaveCount(5);
    page.Events.Select(e => e.EventId).Should().OnlyHaveUniqueItems();
    page.Events.Last().EventId.Should().Be(newest.EventId);
    reader.CaptureSnapshot().Sources.Count(s => s.Kind == LogSourceKind.StartupFallback).Should().Be(4);
    var added = Event(6); File.AppendAllLines(startup, [LogJsonlFormatter.FormatLine(added)]);
    var next = await reader.ReadNewerAsync(request with { Cursor = page.NewerCursor, Direction = LogReadDirection.Newer }, CancellationToken.None);
    next.Events.Should().ContainSingle(e => e.EventId == added.EventId);
  }

  [Fact]
  public async Task Fallback_only_pages_have_real_cursors_and_contiguous_history()
  {
    var startup = Path.Combine(_root, "startup.log");
    File.WriteAllLines(startup, Enumerable.Range(0, 10).Select(i => LogJsonlFormatter.FormatLine(Event(i))));
    var reader = new LogTailReader(Path.Combine(_root, "missing.log"), startup);
    var request = new LogReadRequest(null, LogReadDirection.Latest, 3, 262144);
    var latest = await reader.ReadLatestAsync(request, CancellationToken.None);
    var older = await reader.ReadOlderAsync(request with { Cursor = latest.OlderCursor, Direction = LogReadDirection.Older }, CancellationToken.None);
    latest.OlderCursor.Should().NotBeNull(); older.Events.Should().HaveCount(3);
    latest.Events.Concat(older.Events).Select(e => e.EventId).Should().OnlyHaveUniqueItems();
  }

  private static LogEvent Event(int minute) => LogStructuredSink.CreateComponentEvent(LogSeverity.Warn,
    LogComponentRegistry.Engine, LogChannelRegistry.Policy, EventNameRegistry.GuardStateChanged, "same-text")
    with { Timestamp = new DateTimeOffset(2026, 10, 4, 0, minute, 0, TimeSpan.Zero) };

  [Fact]
  public async Task Older_fallback_pages_progress_beyond_one_byte_window()
  {
    var startup = Path.Combine(_root, "startup.log");
    var main = Path.Combine(_root, "main.log");
    File.WriteAllLines(startup, Enumerable.Range(0, 25).Select(i => LogJsonlFormatter.FormatLine(Event(i * 2))));
    File.WriteAllLines(main, Enumerable.Range(0, 25).Select(i => LogJsonlFormatter.FormatLine(Event(i * 2 + 1))));
    var reader = new LogTailReader(main, startup);
    var request = new LogReadRequest(null, LogReadDirection.Latest, 3, 4096);
    var page = await reader.ReadLatestAsync(request, CancellationToken.None);
    var ids = new List<string>();
    for (var i = 0; i < 25 && page.Events.Count > 0; i++)
    {
      ids.AddRange(page.Events.Select(e => e.EventId));
      page = await reader.ReadOlderAsync(request with { Cursor = page.OlderCursor, Direction = LogReadDirection.Older }, CancellationToken.None);
    }
    ids.Should().HaveCount(50).And.OnlyHaveUniqueItems();
  }
  public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
