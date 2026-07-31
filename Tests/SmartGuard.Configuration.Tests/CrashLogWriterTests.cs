using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class CrashLogWriterTests
{
    [Fact]
    public void Write_persists_crash_entry_with_component_and_full_chain()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var crashPath = Path.Combine(dir, "crash.log");
        var mainPath = Path.Combine(dir, "main.log");
        CrashLogWriter.CrashLogFileOverrideForTests = crashPath;
        try
        {
            var ex = new InvalidOperationException("fatal", new ArgumentException("root-cause"));
            CrashLogWriter.Write("Engine", mainPath, ex);

            var crash = File.ReadAllText(crashPath);
            crash.Should().Contain("[CRASH][Engine]");
            crash.Should().Contain("System.InvalidOperationException");
            crash.Should().Contain("root-cause");
            File.ReadAllText(mainPath).Should().Contain("[CRASH][Engine]");
        }
        finally
        {
            CrashLogWriter.CrashLogFileOverrideForTests = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Write_still_persists_crash_entry_when_main_log_path_is_null()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var crashPath = Path.Combine(dir, "crash.log");
        CrashLogWriter.CrashLogFileOverrideForTests = crashPath;
        try
        {
            var act = () => CrashLogWriter.Write("Tray", null, new Exception("boom"));
            act.Should().NotThrow();
            File.ReadAllText(crashPath).Should().Contain("[CRASH][Tray]");
        }
        finally
        {
            CrashLogWriter.CrashLogFileOverrideForTests = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
