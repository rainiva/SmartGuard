using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView.Tests;

public sealed class StartupFallbackHistoryTests : IDisposable
{
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
