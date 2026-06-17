using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartGuard.Configuration;

public sealed class GuardConfig
{
  [JsonPropertyName("ActivePlanGUID")]
  public Guid ActivePlanGuid { get; set; }

  [JsonPropertyName("BalancedPlanGUID")]
  public Guid BalancedPlanGuid { get; set; }

  [JsonPropertyName("PowerSaverPlanGUID")]
  public Guid PowerSaverPlanGuid { get; set; }

  public int BalancedThresholdSec { get; set; } = 300;
  public int PowerSaverThresholdSec { get; set; } = 900;
  public int LowBatteryPercent { get; set; } = 30;
  public int CheckIntervalSec { get; set; } = 15;
  public int BrightnessRestoreMs { get; set; } = 300;
  public string LogFile { get; set; } = string.Empty;
  public bool Paused { get; set; }
  public long LogMaxBytes { get; set; } = 1_048_576;
  public int BrightnessRetryCount { get; set; } = 3;
  public int BrightnessRetryDelayMs { get; set; } = 100;
  public bool NotifyOnPlanChange { get; set; } = true;
  public int HeartbeatIntervalMin { get; set; } = 10;
  public bool AutoStartEnabled { get; set; } = true;

  public static GuardConfig LoadFromJson(string json)
  {
    var cfg = JsonSerializer.Deserialize<GuardConfig>(json, JsonOptions)
      ?? throw new InvalidOperationException("Invalid config JSON");
    return cfg;
  }

  public static GuardConfig LoadFromFile(string path)
    => LoadFromJson(File.ReadAllText(path));

  public static GuardConfig CreateDefault(string root)
  {
    return new GuardConfig
    {
      ActivePlanGuid = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
      BalancedPlanGuid = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
      PowerSaverPlanGuid = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a"),
      BalancedThresholdSec = 300,
      PowerSaverThresholdSec = 900,
      LowBatteryPercent = 30,
      CheckIntervalSec = 15,
      BrightnessRestoreMs = 300,
      LogFile = Path.Combine(root, "SmartGuard.log"),
      Paused = false,
      LogMaxBytes = 1_048_576,
      BrightnessRetryCount = 3,
      BrightnessRetryDelayMs = 100,
      NotifyOnPlanChange = true,
      HeartbeatIntervalMin = 10,
      AutoStartEnabled = true,
    };
  }

  internal static JsonSerializerOptions JsonOptions { get; } = new()
  {
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
  };
}
