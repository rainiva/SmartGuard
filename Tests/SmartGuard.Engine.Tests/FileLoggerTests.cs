using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class FileLoggerTests
{
    [Fact]
    public void Rotates_log_when_exceeding_max_bytes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var logPath = Path.Combine(dir, "test.log");
        try
        {
            File.WriteAllText(logPath, new string('x', 200));
            FileLogger.RotateIfNeeded(logPath, 100);
            File.Exists(logPath).Should().BeFalse();
            File.Exists(logPath + ".old").Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
