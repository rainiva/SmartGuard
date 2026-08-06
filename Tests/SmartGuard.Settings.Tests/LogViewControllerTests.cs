using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

public class LogViewControllerTests : IDisposable
{
    private readonly string _logPath;
    private readonly string _fallbackPath;

    public LogViewControllerTests()
    {
        _logPath = Path.GetTempFileName();
        _fallbackPath = Path.GetTempFileName();
    }

    public void Dispose()
    {
        try { File.Delete(_logPath); } catch { }
        try { File.Delete(_fallbackPath); } catch { }
    }

    [Fact]
    public void LogViewController_reads_and_formats_log_lines()
    {
        File.WriteAllText(_logPath, "2026-06-19 10:00:00 system started\n2026-06-19 10:01:00 ERROR: disk full\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        var lines = controller.GetFilteredLines();

        lines.Should().HaveCount(2);
        lines[0].Should().Be("[INFO] 2026-06-19 10:00:00 system started");
        lines[1].Should().Be("[ERROR] 2026-06-19 10:01:00 ERROR: disk full");
    }

    [Fact]
    public void LogViewController_filters_by_keyword()
    {
        File.WriteAllText(_logPath, "2026-06-19 10:00:00 system started\n2026-06-19 10:01:00 ERROR: disk full\n2026-06-19 10:02:00 WARN: memory low\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.SearchKeyword = "disk";
        var lines = controller.GetFilteredLines();

        lines.Should().HaveCount(1);
        lines[0].Should().Contain("disk full");
    }

    [Fact]
    public void LogViewController_filters_by_active_tag()
    {
        File.WriteAllText(_logPath, "2026-06-19 10:00:00 system started\n2026-06-19 10:01:00 ERROR: disk full\n2026-06-19 10:02:00 WARN: memory low\n2026-06-19 10:03:00 [监控中] monitoring\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            ActiveTagFilters = ["ERROR", "WARN"],
        };
        var lines = controller.GetFilteredLines();

        lines.Should().HaveCount(2);
        lines.Should().Contain(line => line.Contains("ERROR"));
        lines.Should().Contain(line => line.Contains("WARN"));
        lines.Should().NotContain(line => line.Contains("[INFO]"));
        lines.Should().NotContain(line => line.Contains("[HEART]"));
    }

    [Fact]
    public void RefreshFromDisk_picks_up_appended_lines()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 first line\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.GetFilteredLines().Should().HaveCount(1);

        File.AppendAllText(_logPath, "[INFO] 2026-06-19 10:01:00 second line\n");
        controller.RefreshFromDisk();

        var lines = controller.GetFilteredLines();
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("second line");
    }

    [Fact]
    public void GetSnapshot_reports_tail_truncation_when_log_exceeds_tail_limit()
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < 4000; i++)
            builder.AppendLine($"[INFO] 2026-06-19 10:00:{i % 60:D2} line {i} {new string('x', 80)}");

        File.WriteAllText(_logPath, builder.ToString());

        var controller = new LogViewController(_logPath, _fallbackPath);
        var snapshot = controller.GetSnapshot();

        snapshot.IsTailTruncated.Should().BeTrue();
        snapshot.TotalLineCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetSnapshot_keeps_all_loaded_lines_browsable_for_large_result_sets()
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < 3000; i++)
            builder.AppendLine($"[INFO] 2026-06-19 10:00:{i % 60:D2} entry {i}");

        File.WriteAllText(_logPath, builder.ToString());

        var controller = new LogViewController(_logPath, _fallbackPath);
        var snapshot = controller.GetSnapshot();

        snapshot.DisplayLines.Should().HaveCount(LogTailReader.DefaultTargetEventCount);
        snapshot.HasOlderHistory.Should().BeTrue();
        snapshot.IsDisplayTruncated.Should().BeFalse();
        snapshot.MatchedLineCount.Should().Be(LogTailReader.DefaultTargetEventCount);
    }

    [Fact]
    public void GetSnapshot_keeps_entire_loaded_history_visible_when_follow_tail_is_disabled()
    {
        var builder = new System.Text.StringBuilder();
        var firstTimestamp = new DateTime(2026, 7, 29, 10, 0, 0);
        for (var i = 0; i < 700; i++)
            builder.AppendLine($"[INFO] {firstTimestamp.AddSeconds(i):yyyy-MM-dd HH:mm:ss} [Engine] entry {i}");

        File.WriteAllText(_logPath, builder.ToString());

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            FollowTail = false,
        };

        var snapshot = controller.GetSnapshot();

        snapshot.DisplayLines.Should().HaveCount(LogTailReader.DefaultTargetEventCount);
        snapshot.DisplayLines[^1].Should().Contain("entry 699");
        snapshot.HasOlderHistory.Should().BeTrue();
    }

    [Fact]
    public void RefreshFromDisk_marks_content_changed_only_when_file_grows()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 first line\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.RefreshFromDisk();
        controller.GetSnapshot().ContentChanged.Should().BeFalse();

        File.AppendAllText(_logPath, "[INFO] 2026-06-19 10:01:00 second line\n");
        controller.RefreshFromDisk();
        controller.GetSnapshot().ContentChanged.Should().BeTrue();
    }

    [Fact]
    public void RefreshFromDisk_keeps_archive_truncation_after_current_log_append()
    {
        var archivePath = _logPath + ".20260731.bak";
        try
        {
            File.WriteAllText(
                archivePath,
                "[INFO] 2026-07-31 10:00:00 " + new string('x', 300_000) + "\n");
            File.WriteAllText(_logPath, "[INFO] 2026-08-01 10:00:00 current\n");

            var controller = new LogViewController(_logPath, _fallbackPath);
            controller.GetSnapshot().IsTailTruncated.Should().BeTrue();

            File.AppendAllText(_logPath, "[INFO] 2026-08-01 10:01:00 appended\n");
            controller.RefreshFromDisk();

            controller.GetSnapshot().IsTailTruncated.Should().BeTrue();
        }
        finally
        {
            try { File.Delete(archivePath); } catch { }
        }
    }

    [Fact]
    public void RefreshFromDisk_keeps_archive_history_when_current_log_is_missing()
    {
        var archivePath = _logPath + ".20260731.bak";
        try
        {
            File.Delete(_logPath);
            File.WriteAllText(archivePath, "[INFO] 2026-07-31 10:00:00 archived\n");

            var controller = new LogViewController(_logPath, _fallbackPath);
            controller.GetSnapshot().DisplayLines.Should().ContainSingle(line => line.Contains("archived"));

            controller.RefreshFromDisk();

            controller.GetSnapshot().DisplayLines.Should().ContainSingle(line => line.Contains("archived"));
        }
        finally
        {
            try { File.Delete(archivePath); } catch { }
        }
    }

    [Fact]
    public void RefreshFromDisk_skips_read_when_file_length_unchanged()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 first line\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.RefreshFromDisk();
        LogTailReaderTestMetrics.Reset();

        controller.RefreshFromDisk();

        LogTailReaderTestMetrics.ReadFromOffsetCallCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_does_not_read_log_file_on_creation()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 first line\n");
        LogTailReaderTestMetrics.Reset();

        _ = new LogViewController(_logPath, _fallbackPath);

        LogTailReaderTestMetrics.ReadFromOffsetCallCount.Should().Be(0);
    }

    [Fact]
    public void RefreshFromDisk_trims_cached_text_when_append_exceeds_limit()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 seed line\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.GetFilteredLines();

        for (var i = 0; i < 2500; i++)
        {
            File.AppendAllText(
                _logPath,
                $"[INFO] 2026-06-19 10:{i % 60:D2}:{i % 60:D2} [Engine] padded entry {i} {new string('x', 900)}\n");
            controller.RefreshFromDisk();
        }

        controller.LoadedEventCountForTests.Should().BeLessThanOrEqualTo(LogEventWindow.MaxEventCount);
        controller.GetFilteredLines().Should().Contain(line => line.Contains("padded entry 2499"));
        controller.GetFilteredLines().Should().NotContain(line => line.Contains("seed line"));
    }

    [Fact]
    public void GetSnapshot_reports_log_path_for_status_bar()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 ok\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.GetSnapshot().LogPath.Should().Be(_logPath);
    }

    [Fact]
    public void GetFilteredLines_includes_unparsed_lines_with_raw_prefix()
    {
        File.WriteAllText(_logPath, "plain unstructured line\n[INFO] 2026-06-19 10:00:00 ok\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        var lines = controller.GetFilteredLines();

        lines.Should().Contain("[RAW] plain unstructured line");
        lines.Should().Contain("[INFO] 2026-06-19 10:00:00 ok");
    }

    [Fact]
    public void GetSnapshot_total_line_count_includes_raw_lines()
    {
        File.WriteAllText(_logPath, "plain unstructured line\n[INFO] 2026-06-19 10:00:00 ok\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.GetSnapshot().TotalLineCount.Should().Be(2);
    }

    [Fact]
    public void GetSnapshot_total_line_count_reports_loaded_tail_count_when_log_exceeds_tail()
    {
        // 磁盘真实约 4000 行（>256KB），远超尾部缓存段所能容纳的行数
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < 4000; i++)
            builder.AppendLine($"[INFO] 2026-06-19 10:00:{i % 60:D2} line {i} {new string('x', 60)}");

        File.WriteAllText(_logPath, builder.ToString());
        var diskLineCount = LogLineCounter.CountLines(_logPath);
        diskLineCount.Should().Be(4000);

        var controller = new LogViewController(_logPath, _fallbackPath);
        var snapshot = controller.GetSnapshot();

        // 状态栏口径必须来自同一已载入数据集，而不是混入整盘物理行数。
        snapshot.IsTailTruncated.Should().BeTrue();
        snapshot.TotalLineCount.Should().Be(snapshot.MatchedLineCount);
        snapshot.TotalLineCount.Should().BeLessThan(diskLineCount);
    }

    [Fact]
    public void GetSnapshot_total_line_count_includes_primary_and_fallback_loaded_lines()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 a\n[INFO] 2026-06-19 10:00:01 b\n");
        File.WriteAllText(
            _fallbackPath,
            "[INFO] 2026-06-19 09:00:00 f1\n[INFO] 2026-06-19 09:00:01 f2\n[INFO] 2026-06-19 09:00:02 f3\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.GetSnapshot().TotalLineCount.Should().Be(5);
    }

    [Fact]
    public void GetSnapshot_total_line_count_falls_back_to_fallback_when_primary_missing()
    {
        // 主日志不存在时，已加载集合由 fallback 提供。
        File.Delete(_logPath); // 确保主日志不存在
        File.WriteAllText(_fallbackPath, "[INFO] 2026-06-19 09:00:00 f1\n[INFO] 2026-06-19 09:00:01 f2\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.GetSnapshot().TotalLineCount.Should().Be(2);
    }

    [Fact]
    public void GetSnapshot_reports_empty_state_when_tag_filter_has_no_matches()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 ok\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            ActiveTagFilters = ["ERROR"],
        };

        controller.GetSnapshot().EmptyStateMessage.Should().Be("无匹配结果");
    }

    [Fact]
    public void GetSnapshot_reports_empty_state_when_search_has_no_matches()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 alpha\n[WARN] 2026-06-19 10:01:00 beta\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.SearchKeyword = "missing-term";

        controller.GetSnapshot().EmptyStateMessage.Should().Be("无匹配结果");
    }

    [Fact]
    public void GetSnapshot_has_no_empty_state_when_search_matches()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-19 10:00:00 alpha\n[WARN] 2026-06-19 10:01:00 beta\n");

        var controller = new LogViewController(_logPath, _fallbackPath);
        controller.SearchKeyword = "beta";

        var snapshot = controller.GetSnapshot();
        snapshot.EmptyStateMessage.Should().BeNull();
        snapshot.FilteredLines.Should().HaveCount(1);
    }

    [Fact]
    public void GetFilteredLines_limits_to_today()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 09:00:00 today line\n" +
            "[INFO] 2026-06-20 23:00:00 yesterday line\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            TimeRange = LogViewTimeRange.Today,
            NowProvider = () => new DateTime(2026, 6, 21, 10, 0, 0),
        };

        controller.GetFilteredLines().Should().ContainSingle(line => line.Contains("today line"));
    }

    [Fact]
    public void GetFilteredLines_limits_to_custom_range()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 10:00:00 in range\n" +
            "[INFO] 2026-06-21 12:00:00 out of range\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            TimeRange = LogViewTimeRange.Custom,
            CustomRangeStart = new DateTime(2026, 6, 21, 9, 0, 0),
            CustomRangeEnd = new DateTime(2026, 6, 21, 11, 0, 0),
        };

        controller.GetFilteredLines().Should().ContainSingle(line => line.Contains("in range"));
    }

    [Fact]
    public void GetSnapshot_reports_empty_state_when_time_range_excludes_all_lines()
    {
        File.WriteAllText(_logPath, "[INFO] 2026-06-20 10:00:00 yesterday\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            TimeRange = LogViewTimeRange.Today,
            NowProvider = () => new DateTime(2026, 6, 21, 10, 0, 0),
        };

        controller.GetSnapshot().EmptyStateMessage.Should().Be("无匹配结果");
    }

    [Fact]
    public void GetFilteredLines_supports_case_sensitive_search()
    {
        File.WriteAllText(_logPath,
            "[INFO] 2026-06-21 10:00:00 Battery low\n" +
            "[INFO] 2026-06-21 10:01:00 battery low\n");

        var controller = new LogViewController(_logPath, _fallbackPath)
        {
            SearchKeyword = "Battery",
            SearchCaseSensitive = true,
        };

        controller.GetFilteredLines().Should().ContainSingle(line => line.Contains("Battery"));
    }

    [Fact]
    public void GetFilteredLines_filters_by_operation_id()
    {
      var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      var path = Path.Combine(dir, "test.log");
      File.WriteAllLines(path, new[]
      {
        "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted",
        "[WARN] 2026-08-01 10:01:00 [Engine] RecoveryDegraded op=op-abc",
        "[ERROR] 2026-08-01 10:02:00 [GuardianLoop] IterationError op=op-def"
      });

      try
      {
        var controller = new LogViewController(path);
        controller.ForceRefresh();
        controller.OperationIdFilter = "op-abc";

        var filtered = controller.GetFilteredLines();
        filtered.Should().HaveCount(1);
        filtered[0].Should().Contain("op=op-abc");
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public void GetFilteredLines_filters_by_event_name()
    {
      var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      var path = Path.Combine(dir, "test.log");
      File.WriteAllLines(path, new[]
      {
        "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted",
        "[ERROR] 2026-08-01 10:02:00 [GuardianLoop] IterationError"
      });

      try
      {
        var controller = new LogViewController(path);
        controller.ForceRefresh();
        controller.EventNameFilter = "IterationError";

        var filtered = controller.GetFilteredLines();
        filtered.Should().HaveCount(1);
        filtered[0].Should().Contain("IterationError");
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public void GetFilteredLines_returns_all_when_no_new_filters_set()
    {
      var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      var path = Path.Combine(dir, "test.log");
      File.WriteAllLines(path, new[]
      {
        "[INFO] 2026-08-01 10:00:00 [Engine] AppStarted",
        "[ERROR] 2026-08-01 10:02:00 [GuardianLoop] IterationError"
      });

      try
      {
        var controller = new LogViewController(path);
        controller.ForceRefresh();

        var filtered = controller.GetFilteredLines();
        filtered.Should().HaveCount(2);
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public void GetFilteredLines_combines_keyword_and_operation_id_filter()
    {
      var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      var path = Path.Combine(dir, "test.log");
      File.WriteAllLines(path, new[]
      {
        "[WARN] 2026-08-01 10:01:00 [Engine] RecoveryDegraded op=op-abc",
        "[ERROR] 2026-08-01 10:02:00 [GuardianLoop] IterationError op=op-abc",
        "[INFO] 2026-08-01 10:03:00 [Engine] AppStarted op=op-xyz"
      });

      try
      {
        var controller = new LogViewController(path);
        controller.ForceRefresh();
        controller.OperationIdFilter = "op-abc";
        controller.SearchKeyword = "Error";

        var filtered = controller.GetFilteredLines();
        filtered.Should().HaveCount(1);
        filtered[0].Should().Contain("IterationError");
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public void OperationIdFilter_defaults_to_empty_string()
    {
      var controller = new LogViewController("dummy.log");
      controller.OperationIdFilter.Should().BeEmpty();
      controller.EventNameFilter.Should().BeEmpty();
      controller.ProcessIdFilter.Should().BeNull();
      controller.ThreadIdFilter.Should().BeNull();
    }

  [Fact]
    public void ApplyFilterSpec_maps_time_range_and_level_to_ui_properties()
    {
    var controller = new LogViewController("nonexistent.log");
    var spec = new SmartGuard.Configuration.LogQueryFilterSpec(
      Keyword: "boom", Level: "ERROR", Category: null,
      OperationId: "op-1", TraceId: null,
      ProcessId: 10, ThreadId: 20, EventName: "Evt",
      StartTime: new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero),
      EndTime: new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
      Page: 1, PageSize: 50);
    controller.ApplyFilterSpec(spec);
    controller.SearchKeyword.Should().Be("boom");
    controller.OperationIdFilter.Should().Be("op-1");
    controller.TimeRange.Should().Be(LogViewTimeRange.Custom);
    controller.CustomRangeStart.Should().Be(spec.StartTime!.Value.LocalDateTime);
    controller.CustomRangeEnd.Should().Be(spec.EndTime!.Value.LocalDateTime);
    controller.ActiveTagFilters.Should().ContainSingle().Which.Should().Be("ERROR");
  }

  [Fact]
  public async Task History_boundary_refreshes_can_reach_both_physical_ends()
  {
    var builder = new System.Text.StringBuilder();
    for (var i = 0; i < 4000; i++)
      builder.AppendLine($"[INFO] 2026-08-01 10:00:00 entry-{i:D4} {new string('x', 80)}");

    File.WriteAllText(_logPath, builder.ToString());

    var controller = new LogViewController(_logPath, _fallbackPath);
    await controller.RefreshAsync(LogViewRefreshKind.Latest, CancellationToken.None);

    var start = await controller.RefreshAsync(
      LogViewRefreshKind.HistoryStart,
      CancellationToken.None);

    start.Snapshot.HasOlderHistory.Should().BeFalse();
    start.Snapshot.DisplayLines.Should().Contain(line => line.Contains("0000"));

    var end = await controller.RefreshAsync(
      LogViewRefreshKind.HistoryEnd,
      CancellationToken.None);

    end.Snapshot.DisplayLines.Should().Contain(line => line.Contains("3999"));
  }
}
