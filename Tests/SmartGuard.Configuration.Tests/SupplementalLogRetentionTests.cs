using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SmartGuard.Configuration.Tests;

[Collection("LoggingFallbackTests")]
public sealed class SupplementalLogRetentionTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "SmartGuardSupplemental_" + Guid.NewGuid().ToString("N"));
  public SupplementalLogRetentionTests() => Directory.CreateDirectory(_root);

  [Fact]
  public void Independent_maintenance_repairs_legacy_tail_and_expires_only_owned_archives()
  {
    var path = Path.Combine(_root, "crash.log");
    File.WriteAllText(path, new string('a', 2 * 1024 * 1024) + "\nlatest-complete\npartial", new UTF8Encoding(false));
    var old = path + ".20260101.001.bak";
    var neighbor = path + ".neighbor.20260101.001.bak";
    File.WriteAllText(old, "old"); File.WriteAllText(neighbor, "unrelated");
    var method = typeof(SmartGuardLogWriter).GetMethod("MaintainSupplementalLog");
    method.Should().NotBeNull();
    var result = method!.Invoke(null, [path, new DateTime(2026, 10, 4)])!;
    result.GetType().GetProperty("Completed")!.GetValue(result).Should().Be(true);
    File.Exists(old).Should().BeFalse(); File.Exists(neighbor).Should().BeTrue();
    new FileInfo(path).Length.Should().BeLessThanOrEqualTo(1024 * 1024);
    File.ReadAllText(path).Should().Contain("latest-complete").And.NotContain("partial");
    Directory.GetFiles(_root, "*.bak").Select(p => new FileInfo(p).Length).Should().OnlyContain(bytes => bytes <= 1024 * 1024);
  }

  [Fact]
  public void Maintenance_reports_incomplete_at_candidate_cap()
  {
    var path = Path.Combine(_root, "crash.log");
    for (var i = 1; i <= 40; i++) File.WriteAllText(path + $".20260101.{i:D3}.bak", "old");
    var method = typeof(SmartGuardLogWriter).GetMethod("MaintainSupplementalLog");
    method.Should().NotBeNull();
    var result = method!.Invoke(null, [path, new DateTime(2026, 10, 4)])!;
    result.GetType().GetProperty("Completed")!.GetValue(result).Should().Be(false);
    result.GetType().GetProperty("ExaminedFiles")!.GetValue(result).Should().Be(32);
    Directory.GetFiles(_root, "*.bak").Should().HaveCount(8);
  }

  [Theory]
  [InlineData("startup")]
  [InlineData("emergency")]
  [InlineData("crash")]
  [InlineData("crash-ring")]
  public void Existing_near_limit_file_and_new_batch_never_exceed_active_budget(string kind)
  {
    var path = Path.Combine(_root, kind + ".log");
    File.WriteAllText(path, new string('a', 1024 * 1024 - 128) + "\n", new UTF8Encoding(false));
    var line = string.Concat(Enumerable.Repeat("中文😀", 20000));
    if (kind == "startup") LogFallbackChain.Write(() => false, path, line).Should().BeTrue();
    if (kind == "emergency") EmergencySyncWriter.TryWrite(path, [line]).Should().BeTrue();
    if (kind == "crash-ring") { var ring = new CrashRingBuffer(); ring.Add(line); ring.FlushToSync(path).Should().BeTrue(); }
    if (kind == "crash")
    {
      var previous = CrashLogWriter.CrashLogFileOverrideForTests;
      try { CrashLogWriter.CrashLogFileOverrideForTests = path; CrashLogWriter.Write("Engine", null, new Exception(line)); }
      finally { CrashLogWriter.CrashLogFileOverrideForTests = previous; }
    }
    new FileInfo(path).Length.Should().BeLessThanOrEqualTo(1024 * 1024);
    var bytes = File.ReadAllBytes(path);
    new UTF8Encoding(false, true).GetString(bytes).Should().NotContain("�");
    bytes.Take(3).Should().NotEqual(new byte[] { 239, 187, 191 });
  }

  [Fact]
  public void Fallback_budget_confirms_only_selected_original_events_and_keeps_legal_jsonl()
  {
    var path = Path.Combine(_root, "startup.log");
    var events = Enumerable.Range(0, 200).Select(i => LogStructuredSink.CreateComponentEvent(
      LogSeverity.Warn, LogComponentRegistry.Engine, LogChannelRegistry.Policy, EventNameRegistry.GuardStateChanged,
      "event-" + i + new string('界', 2000))).ToArray();
    var result = LogFallbackChain.WriteBatch(path, events, false);
    result.FallbackWritten.Should().BeGreaterThan(0).And.BeLessThan(events.Length);
    result.Lost.Should().Be(events.Length - result.FallbackWritten);
    new FileInfo(path).Length.Should().BeLessThanOrEqualTo(128 * 1024);
    var persisted = File.ReadLines(path).Select(line => { LogJsonlFormatter.TryParseLine(line, out var item).Should().BeTrue(); return item!; }).ToArray();
    var latestId = events[^1].EventId;
    persisted.Should().Contain(e => e.EventId == latestId);
    persisted.Count(e => events.Any(original => original.EventId == e.EventId)).Should().Be(result.FallbackWritten);
    persisted.Should().Contain(e => e.Message.Contains("截断", StringComparison.Ordinal));
  }

  [Fact]
  public void Ring_limits_utf8_entry_and_total_bytes_without_breaking_structured_lines()
  {
    var ring = new CrashRingBuffer(1000);
    var logEvent = LogStructuredSink.CreateComponentEvent(LogSeverity.Error, LogComponentRegistry.Engine,
      LogChannelRegistry.Recovery, EventNameRegistry.IterationError, string.Concat(Enumerable.Repeat("中文😀", 30000)));
    var line = LogJsonlFormatter.FormatLine(logEvent);
    for (var i = 0; i < 1000; i++) ring.Add(line);
    var snapshot = ring.Drain();
    snapshot.Select(item => Encoding.UTF8.GetByteCount(item)).Should().OnlyContain(bytes => bytes <= 4096);
    snapshot.Sum(item => Encoding.UTF8.GetByteCount(item) + Environment.NewLine.Length).Should().BeLessThanOrEqualTo(2 * 1024 * 1024);
    foreach (var item in snapshot) LogJsonlFormatter.TryParseLine(item, out _).Should().BeTrue();
    snapshot.Should().Contain(item => item.Contains("截断", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Emergency_does_not_wait_for_mutex_or_bypass_ownership()
  {
    var path = Path.Combine(_root, "crash-ring.log");
    var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var release = new ManualResetEventSlim();
    var owner = Task.Run(() =>
    {
      using var mutex = new Mutex(false, SmartGuardLogWriter.MutexNameForTests(path));
      mutex.WaitOne(); acquired.SetResult(); release.Wait(); mutex.ReleaseMutex();
    });
    await acquired.Task;
    try
    {
      var timer = Stopwatch.StartNew();
      EmergencySyncWriter.TryWrite(path, ["event"]).Should().BeFalse();
      timer.ElapsedMilliseconds.Should().BeLessThan(500);
      File.Exists(path).Should().BeFalse();
    }
    finally { release.Set(); await owner; }
  }

  public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
