using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings.Tests;

public class LogLineCounterTests : IDisposable
{
    private readonly string _logPath;

    public LogLineCounterTests()
    {
        _logPath = Path.GetTempFileName();
    }

    public void Dispose()
    {
        try { File.Delete(_logPath); } catch { }
    }

    [Fact]
    public void CountLines_returns_line_count_for_file_with_trailing_newline()
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < 2500; i++)
            builder.Append($"line {i}\n");

        File.WriteAllText(_logPath, builder.ToString());

        LogLineCounter.CountLines(_logPath).Should().Be(2500);
    }

    [Fact]
    public void CountLines_returns_zero_for_empty_file()
    {
        File.WriteAllText(_logPath, string.Empty);

        LogLineCounter.CountLines(_logPath).Should().Be(0);
    }

    [Fact]
    public void CountLines_counts_last_line_without_trailing_newline()
    {
        File.WriteAllText(_logPath, "a\nb");

        LogLineCounter.CountLines(_logPath).Should().Be(2);
    }

    [Fact]
    public void CountLines_treats_crlf_and_lf_consistently()
    {
        var crlfPath = Path.GetTempFileName();
        var lfPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(crlfPath, "a\r\nb\r\n");
            File.WriteAllText(lfPath, "a\nb\n");

            LogLineCounter.CountLines(crlfPath).Should().Be(2);
            LogLineCounter.CountLines(lfPath).Should().Be(2);
        }
        finally
        {
            try { File.Delete(crlfPath); } catch { }
            try { File.Delete(lfPath); } catch { }
        }
    }

    [Fact]
    public void CountLines_returns_zero_when_file_does_not_exist()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".log");

        LogLineCounter.CountLines(missing).Should().Be(0);
    }

    [Fact]
    public void CountLines_counts_blank_middle_lines_as_physical_lines()
    {
        // 物理行计数：ReadLine 逐行计数，中间空行也计入（明示与旧 RemoveEmptyEntries 语义差异）
        File.WriteAllText(_logPath, "a\n\nb\n");

        LogLineCounter.CountLines(_logPath).Should().Be(3);
    }
}
