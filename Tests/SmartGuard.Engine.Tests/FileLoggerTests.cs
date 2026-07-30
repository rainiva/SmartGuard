using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class FileLoggerTests
{
    [Fact]
    public void Rotates_log_to_dated_archive_when_exceeding_max_bytes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "test.log");
        var now = new DateTime(2026, 6, 16, 10, 0, 0);
        try
        {
            File.WriteAllText(logPath, new string('x', 200));
            FileLogger.RotateIfNeeded(logPath, 100, now);
            File.Exists(logPath).Should().BeFalse();
            File.Exists(logPath + ".20260616.bak").Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Rotates_log_to_previous_day_archive_when_calendar_day_changes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "test.log");
        try
        {
            File.WriteAllText(logPath, "yesterday");
            File.SetLastWriteTime(logPath, new DateTime(2026, 6, 15, 23, 0, 0));
            FileLogger.RotateDailyIfNeeded(logPath, new DateTime(2026, 6, 16, 0, 5, 0));
            File.Exists(logPath).Should().BeFalse();
            File.Exists(logPath + ".20260615.bak").Should().BeTrue();
            File.ReadAllText(logPath + ".20260615.bak").Should().Be("yesterday");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PruneExpiredArchives_deletes_archives_older_than_seven_days()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "test.log");
        var oldArchive = logPath + ".20260601.bak";
        var keepArchive = logPath + ".20260610.bak";
        try
        {
            File.WriteAllText(oldArchive, "old");
            File.WriteAllText(keepArchive, "keep");
            FileLogger.PruneExpiredArchives(logPath, retainDays: 7, new DateTime(2026, 6, 16, 12, 0, 0));
            File.Exists(oldArchive).Should().BeFalse();
            File.Exists(keepArchive).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Write_persists_formatted_line_with_level()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "test.log");
        try
        {
            FileLogger.Write(LogLevel.Info, logPath, "engine started", long.MaxValue);
            var line = File.ReadAllText(logPath).Trim();
            line.Should().Contain("[INFO]");
            line.Should().Contain("engine started");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Write_does_not_throw_when_log_path_is_unwritable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "locked.log");
        Directory.CreateDirectory(logPath); // 让日志路径本身是目录，制造写入必然失败
        try
        {
            var act = () => FileLogger.Write(LogLevel.Info, logPath, "boom", long.MaxValue);
            act.Should().NotThrow();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Write_does_not_throw_when_rotation_target_is_unwritable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "test.log");
        var now = new DateTime(2026, 6, 16, 10, 0, 0);
        try
        {
            File.WriteAllText(logPath, new string('x', 200));
            Directory.CreateDirectory(logPath + ".20260616.bak"); // 归档目标是目录，轮转必然失败
            var act = () => FileLogger.Write(LogLevel.Info, logPath, "next", 100, now);
            act.Should().NotThrow();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Write_is_reentrancy_guarded_and_does_not_recurse()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "test.log");
        var probeCalls = 0;
        FileLogger.ReentrancyProbeForTests = () =>
        {
            probeCalls++;
            if (probeCalls == 1)
                FileLogger.Write(LogLevel.Error, logPath, "nested", long.MaxValue); // 内层再次进入应被防递归短路
        };
        try
        {
            var act = () => FileLogger.Write(LogLevel.Info, logPath, "outer", long.MaxValue);
            act.Should().NotThrow();
            probeCalls.Should().Be(1, "防递归应阻止内层 Write 再次触发 probe");
        }
        finally
        {
            FileLogger.ReentrancyProbeForTests = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
