using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogViewToolbarActionsTests
{
    [Fact]
    public void BuildVisibleText_joins_display_lines()
    {
        var snapshot = new LogViewSnapshot(
            ["[INFO] 2026-06-21 10:00:00 alpha", "[WARN] 2026-06-21 10:01:00 beta"],
            2,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false);

        LogViewToolbarActions.BuildVisibleText(snapshot)
            .Should().Be("[INFO] 2026-06-21 10:00:00 alpha" + Environment.NewLine + "[WARN] 2026-06-21 10:01:00 beta");
    }

    [Fact]
    public void BuildVisibleText_uses_empty_state_message_when_present()
    {
        var snapshot = new LogViewSnapshot(
            [],
            2,
            false,
            @"C:\SmartGuard\SmartGuard.log",
            false,
            "missing",
            "无匹配结果");

        LogViewToolbarActions.BuildVisibleText(snapshot).Should().Be("无匹配结果");
    }

    [Fact]
    public void ResolveLogFilePath_prefers_primary_when_exists()
    {
        var primary = Path.GetTempFileName();
        var fallback = Path.GetTempFileName();
        try
        {
            File.WriteAllText(primary, "log");

            LogViewToolbarActions.ResolveLogFilePath(primary, fallback).Should().Be(primary);
        }
        finally
        {
            try { File.Delete(primary); } catch { }
            try { File.Delete(fallback); } catch { }
        }
    }

    [Fact]
    public void ResolveLogFilePath_uses_fallback_when_primary_missing()
    {
        var primary = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".log");
        var fallback = Path.GetTempFileName();
        try
        {
            File.WriteAllText(fallback, "log");

            LogViewToolbarActions.ResolveLogFilePath(primary, fallback).Should().Be(fallback);
        }
        finally
        {
            try { File.Delete(fallback); } catch { }
        }
    }

    [Fact]
    public void ExportVisibleText_writes_utf8_without_bom()
    {
        var path = Path.GetTempFileName();
        try
        {
            LogViewToolbarActions.ExportVisibleText("[INFO] exported line", path);

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3)
                bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
            File.ReadAllText(path, System.Text.Encoding.UTF8).Should().Be("[INFO] exported line");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void CreateRevealLogFileProcessStartInfo_targets_explorer_with_select_argument()
    {
        var startInfo = LogViewToolbarActions.CreateRevealLogFileProcessStartInfo(@"C:\SmartGuard\SmartGuard.log");

        startInfo.FileName.Should().Be("explorer.exe");
        startInfo.Arguments.Should().Contain("/select,");
        startInfo.Arguments.Should().Contain("SmartGuard.log");
        startInfo.UseShellExecute.Should().BeTrue();
    }
}
