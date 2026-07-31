using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogFallbackChainTests
{
    [Fact]
    public void Write_uses_primary_only_when_primary_succeeds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var fallbackPath = Path.Combine(dir, "startup.log");
        var emergencyPath = Path.Combine(dir, "emergency.log");
        LogFallbackChain.EmergencyLogFileOverrideForTests = emergencyPath;
        try
        {
            LogFallbackChain.Write(() => true, fallbackPath, "[INFO] hi");
            File.Exists(fallbackPath).Should().BeFalse();
            File.Exists(emergencyPath).Should().BeFalse();
        }
        finally
        {
            LogFallbackChain.EmergencyLogFileOverrideForTests = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Write_falls_back_to_startup_when_primary_fails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var fallbackPath = Path.Combine(dir, "startup.log");
        var emergencyPath = Path.Combine(dir, "emergency.log");
        LogFallbackChain.EmergencyLogFileOverrideForTests = emergencyPath;
        try
        {
            LogFallbackChain.Write(() => false, fallbackPath, "[INFO] hi");
            File.ReadAllText(fallbackPath).Should().Contain("[LOG-FALLBACK] [INFO] hi");
            File.Exists(emergencyPath).Should().BeFalse();
        }
        finally
        {
            LogFallbackChain.EmergencyLogFileOverrideForTests = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Write_falls_back_to_temp_emergency_when_primary_and_startup_fail()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var fallbackPath = Path.Combine(dir, "startup-as-dir.log");
        Directory.CreateDirectory(fallbackPath); // 回退层不可写
        var emergencyPath = Path.Combine(dir, "emergency.log");
        LogFallbackChain.EmergencyLogFileOverrideForTests = emergencyPath;
        try
        {
            var act = () => LogFallbackChain.Write(() => false, fallbackPath, "[INFO] hi");
            act.Should().NotThrow();
            File.ReadAllText(emergencyPath).Should().Contain("[LOG-EMERGENCY] [INFO] hi");
        }
        finally
        {
            LogFallbackChain.EmergencyLogFileOverrideForTests = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
