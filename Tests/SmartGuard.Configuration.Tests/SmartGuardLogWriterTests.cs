using System.Collections.Concurrent;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class SmartGuardLogWriterTests
{
    [Fact]
    public void TryAppendLine_appends_line_and_returns_true()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "unified.log");
        try
        {
            var ok = SmartGuardLogWriter.TryAppendLine(path, "hello-line");
            ok.Should().BeTrue();
            File.ReadAllText(path).Should().Contain("hello-line");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryAppendLine_returns_false_and_does_not_throw_when_path_unwritable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "as-dir.log");
        Directory.CreateDirectory(path); // 路径是目录，追加必然失败
        try
        {
            var result = false;
            var act = () => result = SmartGuardLogWriter.TryAppendLine(path, "x");
            act.Should().NotThrow();
            result.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryAppendLine_survives_concurrent_writers_without_loss()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "concurrent.log");
        try
        {
            var results = new ConcurrentBag<bool>();
            Parallel.For(0, 50, i => results.Add(SmartGuardLogWriter.TryAppendLine(path, $"line-{i}")));
            results.Should().OnlyContain(r => r == true);
            var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            lines.Should().HaveCount(50, "50 个并发写入不得丢行");
            lines.Should().OnlyContain(l => l.StartsWith("line-"), "行内容不得交错损坏");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TryAppendLine_under_multi_process_style_stress_loses_no_lines()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "stress.log");
        try
        {
            var results = new ConcurrentBag<bool>();
            var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                    results.Add(SmartGuardLogWriter.TryAppendLine(path, $"writer{w}-line{i:D3}"));
            })).ToArray();
            Task.WaitAll(writers);

            results.Should().OnlyContain(r => r == true, "4 写者持续交错写入不得失败");
            var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            lines.Should().HaveCount(400, "4 写者 × 100 行不得丢行");
            lines.Should().OnlyContain(
                l => System.Text.RegularExpressions.Regex.IsMatch(l, "^writer[0-3]-line[0-9]{3}$"),
                "行内容不得交错损坏（决策 D3 的评估证据）");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
