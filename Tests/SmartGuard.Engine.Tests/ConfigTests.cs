using System.Text.Json;
using SmartGuard.Engine.Config;

namespace SmartGuard.Engine.Tests;

public class ConfigTests
{
    [Fact]
    public void Deserializes_existing_json_contract()
    {
        var json = """
        {
            "ActivePlanGUID": "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
            "BalancedPlanGUID": "381b4222-f694-41f0-9685-ff5bb260df2e",
            "PowerSaverPlanGUID": "a1841308-3541-4fab-bc81-f71556f20b4a",
            "BalancedThresholdSec": 300,
            "PowerSaverThresholdSec": 900,
            "LowBatteryPercent": 30,
            "CheckIntervalSec": 15,
            "BrightnessRestoreMs": 300,
            "LogFile": "D:\\Project\\SmartGuard\\SmartGuard.log",
            "Paused": false,
            "LogMaxBytes": 1048576,
            "BrightnessRetryCount": 3,
            "BrightnessRetryDelayMs": 100,
            "NotifyOnPlanChange": true,
            "HeartbeatIntervalMin": 30,
            "AutoStartEnabled": true
        }
        """;

        var cfg = GuardConfig.LoadFromJson(json);
        cfg.ActivePlanGuid.Should().Be(Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));
        cfg.BalancedThresholdSec.Should().Be(300);
        cfg.LogFile.Should().Be(@"D:\Project\SmartGuard\SmartGuard.log");
        cfg.HeartbeatIntervalMin.Should().Be(30);
    }

    [Fact]
    public void Default_log_file_uses_SmartGuard_log()
    {
        var cfg = GuardConfig.CreateDefault(@"D:\Project\SmartGuard");
        cfg.LogFile.Should().Be(Path.Combine(@"D:\Project\SmartGuard", "SmartGuard.log"));
    }
}
