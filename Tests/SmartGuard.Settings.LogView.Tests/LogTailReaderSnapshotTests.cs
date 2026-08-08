using System.Text;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.LogView.Tests;

public sealed class LogTailReaderSnapshotTests
{
  private static readonly UTF8Encoding Utf8NoBom = new(false);

  [Fact]
  public void CaptureSnapshot_records_catalog_order_identity_kind_and_length()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var archivePath = logPath + ".20260807.bak";
    var startupPath = Path.Combine(root, "startup.log");
    File.WriteAllText(archivePath, LegacyLine(8, 0, 0, "archive") + "\n", Utf8NoBom);
    File.WriteAllText(logPath, LegacyLine(9, 0, 0, "current") + "\n", Utf8NoBom);
    File.WriteAllText(startupPath, "2026-08-01 07:00:00 startup\n", Utf8NoBom);

    try
    {
      var reader = new LogTailReader(logPath, startupPath);
      var snapshot = SnapshotReader(reader).CaptureSnapshot();
      var catalog = LogSourceCatalog.Build(logPath, startupPath);
      var expectedSources = catalog.MainHistoryOldestFirst
        .Concat(catalog.StartupFallback is null ? [] : [catalog.StartupFallback])
        .ToArray();

      snapshot.Sources.Should().HaveCount(expectedSources.Length);
      snapshot.Sources.Select(entry => entry.Source).Should().Equal(expectedSources.Select(source => source.Identity));
      snapshot.Sources.Select(entry => entry.Kind).Should().Equal(expectedSources.Select(source => source.Kind));
      snapshot.Sources.Select(entry => entry.CapturedLength).Should().Equal(expectedSources.Select(source => new FileInfo(source.FilePath).Length));
      snapshot.Sources.Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.BoundaryFingerprint));
      snapshot.Fingerprint.Should().NotBeNullOrWhiteSpace();
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public void Snapshot_scan_ignores_lines_appended_after_capture()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    File.WriteAllText(
      logPath,
      string.Join('\n', [
        LegacyLine(10, 0, 0, "first"),
        LegacyLine(10, 0, 1, "second"),
      ]) + "\n",
      Utf8NoBom);

    try
    {
      var reader = new LogTailReader(logPath);
      var snapshotReader = SnapshotReader(reader);
      var snapshot = snapshotReader.CaptureSnapshot();

      File.AppendAllText(logPath, LegacyLine(10, 0, 2, "after-capture") + "\n", Utf8NoBom);

      var lines = snapshotReader
        .EnumeratePhysicalLines(AllSources(snapshot), snapshot, LogReadDirection.Newer, cursor: null, CancellationToken.None)
        .ToList();

      lines.Select(line => line.Text).Should().Equal(
        LegacyLine(10, 0, 0, "first"),
        LegacyLine(10, 0, 1, "second"));
      lines.Should().NotContain(line => line.Text.Contains("after-capture", StringComparison.Ordinal));
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public void Snapshot_scan_resolves_rotated_source_by_identity()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var archivePath = logPath + ".20260808.bak";
    File.WriteAllText(
      logPath,
      string.Join('\n', [
        LegacyLine(10, 0, 0, "before-rotate-1"),
        LegacyLine(10, 0, 1, "before-rotate-2"),
      ]) + "\n",
      Utf8NoBom);

    try
    {
      var reader = new LogTailReader(logPath);
      var snapshotReader = SnapshotReader(reader);
      var snapshot = snapshotReader.CaptureSnapshot();
      var capturedCurrent = snapshot.Sources.Single(entry => entry.Kind == LogSourceKind.Current);

      File.Move(logPath, archivePath);
      File.WriteAllText(logPath, LegacyLine(10, 0, 2, "after-rotate") + "\n", Utf8NoBom);

      var lines = snapshotReader
        .EnumeratePhysicalLines(
          new LogReadScope([capturedCurrent.Source]),
          snapshot,
          LogReadDirection.Newer,
          cursor: null,
          CancellationToken.None)
        .ToList();

      lines.Should().HaveCount(2);
      lines.Select(line => line.Source).Should().OnlyContain(source => source == capturedCurrent.Source);
      lines.Select(line => line.Text).Should().Equal(
        LegacyLine(10, 0, 0, "before-rotate-1"),
        LegacyLine(10, 0, 1, "before-rotate-2"));
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public void Snapshot_scan_rejects_truncate_and_regrow_with_same_prefix_and_length()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var original = LongSnapshotBody("original-tail");
    File.WriteAllText(logPath, original, Utf8NoBom);

    try
    {
      var reader = new LogTailReader(logPath);
      var snapshotReader = SnapshotReader(reader);
      var snapshot = snapshotReader.CaptureSnapshot();

      using (var stream = new FileStream(logPath, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite))
      using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
      {
        writer.Write(LongSnapshotBody("changed-tail!"));
      }

      Action act = () => snapshotReader
        .EnumeratePhysicalLines(AllSources(snapshot), snapshot, LogReadDirection.Newer, cursor: null, CancellationToken.None)
        .ToList();

      act.Should().Throw<LogSnapshotInvalidatedException>();
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public void Snapshot_scan_rejects_rewritten_archive_with_same_identity_but_changed_boundary()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var archivePath = logPath + ".20260807.bak";
    File.WriteAllText(archivePath, LongSnapshotBody("archive-tail-1"), Utf8NoBom);
    File.WriteAllText(logPath, LegacyLine(10, 0, 0, "current") + "\n", Utf8NoBom);

    try
    {
      var reader = new LogTailReader(logPath);
      var snapshotReader = SnapshotReader(reader);
      var snapshot = snapshotReader.CaptureSnapshot();
      var archiveSource = snapshot.Sources.Single(entry => entry.Kind == LogSourceKind.Archive);

      using (var stream = new FileStream(archivePath, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite))
      using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
      {
        writer.Write(LongSnapshotBody("archive-tail-2"));
      }

      Action act = () => snapshotReader
        .EnumeratePhysicalLines(
          new LogReadScope([archiveSource.Source]),
          snapshot,
          LogReadDirection.Newer,
          cursor: null,
          CancellationToken.None)
        .ToList();

      act.Should().Throw<LogSnapshotInvalidatedException>();
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public void Snapshot_scan_includes_startup_compatibility_without_duplicates()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var startupPath = Path.Combine(root, "startup.log");
    File.WriteAllText(startupPath, "2026-08-01 07:00:00 startup-only\n", Utf8NoBom);
    File.WriteAllText(logPath, LegacyLine(10, 0, 0, "current-only") + "\n", Utf8NoBom);

    try
    {
      var reader = new LogTailReader(logPath, startupPath);
      var snapshotReader = SnapshotReader(reader);
      var snapshot = snapshotReader.CaptureSnapshot();

      var lines = snapshotReader
        .EnumeratePhysicalLines(AllSources(snapshot), snapshot, LogReadDirection.Newer, cursor: null, CancellationToken.None)
        .ToList();

      lines.Should().ContainSingle(line => line.Text.Contains("startup-only", StringComparison.Ordinal));
      lines.Should().ContainSingle(line => line.Text.Contains("current-only", StringComparison.Ordinal));
      lines.Select(line => (line.Source, line.ByteOffset, line.Text, line.Ordinal)).Should().OnlyHaveUniqueItems();
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public void Snapshot_scan_from_cursor_is_exclusive_and_crosses_archives()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    var archivePath = logPath + ".20260807.bak";
    File.WriteAllText(
      archivePath,
      string.Join('\n', [
        LegacyLine(8, 0, 0, "archive-1"),
        LegacyLine(8, 0, 1, "archive-2"),
      ]) + "\n",
      Utf8NoBom);
    File.WriteAllText(
      logPath,
      string.Join('\n', [
        LegacyLine(9, 0, 0, "current-1"),
        LegacyLine(9, 0, 1, "current-2"),
      ]) + "\n",
      Utf8NoBom);

    try
    {
      var reader = new LogTailReader(logPath);
      var snapshotReader = SnapshotReader(reader);
      var snapshot = snapshotReader.CaptureSnapshot();
      var allLines = snapshotReader
        .EnumeratePhysicalLines(AllSources(snapshot), snapshot, LogReadDirection.Newer, cursor: null, CancellationToken.None)
        .ToList();
      var archiveCursorLine = allLines.Single(line => line.Text.Contains("archive-2", StringComparison.Ordinal));
      var currentCursorLine = allLines.Single(line => line.Text.Contains("current-1", StringComparison.Ordinal));

      var newer = snapshotReader
        .EnumeratePhysicalLines(
          AllSources(snapshot),
          snapshot,
          LogReadDirection.Newer,
          new LogReadCursor(archiveCursorLine.Source, archiveCursorLine.ByteOffset, null),
          CancellationToken.None)
        .Select(line => line.Text)
        .ToList();

      var latest = snapshotReader
        .EnumeratePhysicalLines(
          AllSources(snapshot),
          snapshot,
          LogReadDirection.Latest,
          new LogReadCursor(currentCursorLine.Source, currentCursorLine.ByteOffset, null),
          CancellationToken.None)
        .Select(line => line.Text)
        .ToList();

      newer.Should().Equal(
        LegacyLine(9, 0, 0, "current-1"),
        LegacyLine(9, 0, 1, "current-2"));
      latest.Should().Equal(
        LegacyLine(8, 0, 1, "archive-2"),
        LegacyLine(8, 0, 0, "archive-1"));
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  [Fact]
  public void Snapshot_scan_honors_cancellation_on_every_physical_line()
  {
    var root = CreateRoot();
    var logPath = Path.Combine(root, "SmartGuard.log");
    File.WriteAllText(
      logPath,
      string.Join('\n', Enumerable.Range(0, 5).Select(i => LegacyLine(10, 0, i, $"line-{i}"))) + "\n",
      Utf8NoBom);

    try
    {
      var reader = new LogTailReader(logPath);
      var snapshotReader = SnapshotReader(reader);
      var snapshot = snapshotReader.CaptureSnapshot();
      using var cts = new CancellationTokenSource();
      using var enumerator = snapshotReader
        .EnumeratePhysicalLines(AllSources(snapshot), snapshot, LogReadDirection.Newer, cursor: null, cts.Token)
        .GetEnumerator();

      enumerator.MoveNext().Should().BeTrue();
      cts.Cancel();

      Action act = () => enumerator.MoveNext();
      act.Should().Throw<OperationCanceledException>();
    }
    finally
    {
      CleanupRoot(root);
    }
  }

  private static ILogSnapshotPhysicalLineSource SnapshotReader(LogTailReader reader)
  {
    var snapshotReader = (object)reader as ILogSnapshotPhysicalLineSource;
    snapshotReader.Should().NotBeNull("Task 2 requires LogTailReader to implement the snapshot physical-line contract");
    return snapshotReader!;
  }

  private static LogReadScope AllSources(LogSourceSnapshot snapshot)
    => new(snapshot.Sources.Select(entry => entry.Source).ToArray());

  private static string LongSnapshotBody(string tailMarker)
    => new string('A', 5000) + "\n" + tailMarker + "\n";

  private static string LegacyLine(int hour, int minute, int second, string message)
    => $"[INFO] 2026-08-01 {hour:D2}:{minute:D2}:{second:D2} [Engine] {message}";

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
