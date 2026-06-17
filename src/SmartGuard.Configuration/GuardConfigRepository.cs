using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartGuard.Configuration;

public sealed class GuardConfigRepository(string configPath)
{
  private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

  public GuardConfig? TryLoad()
  {
    if (!File.Exists(configPath)) return null;
    try
    {
      var node = JsonNode.Parse(File.ReadAllText(configPath));
      if (node is null) return null;
      return node.Deserialize<GuardConfig>(GuardConfig.JsonOptions);
    }
    catch
    {
      return null;
    }
  }

  public GuardConfig LoadOrDefault(string root)
    => TryLoad() ?? GuardConfig.CreateDefault(root);

  public void Save(GuardConfig config)
  {
    var node = LoadOrCreateNode();
    ApplyConfig(node, config);
    WriteNode(node);
  }

  public void UpdatePaused(bool paused)
  {
    var node = LoadOrCreateNode();
    node["Paused"] = paused;
    WriteNode(node);
  }

  public void AppendInfoLog(string message, string fallbackLogPath)
  {
    var config = TryLoad();
    var logPath = config?.LogFile;
    if (string.IsNullOrWhiteSpace(logPath)) logPath = fallbackLogPath;
    var dir = Path.GetDirectoryName(logPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    var line = $"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
    File.AppendAllText(logPath, line);
  }

  private JsonObject LoadOrCreateNode()
  {
    if (!File.Exists(configPath)) return new JsonObject();
    try
    {
      return JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? new JsonObject();
    }
    catch
    {
      return new JsonObject();
    }
  }

  private static void ApplyConfig(JsonObject node, GuardConfig config)
  {
    node["ActivePlanGUID"] = config.ActivePlanGuid.ToString();
    node["BalancedPlanGUID"] = config.BalancedPlanGuid.ToString();
    node["PowerSaverPlanGUID"] = config.PowerSaverPlanGuid.ToString();
    node["BalancedThresholdSec"] = config.BalancedThresholdSec;
    node["PowerSaverThresholdSec"] = config.PowerSaverThresholdSec;
    node["LowBatteryPercent"] = config.LowBatteryPercent;
    node["CheckIntervalSec"] = config.CheckIntervalSec;
    node["BrightnessRestoreMs"] = config.BrightnessRestoreMs;
    node["LogFile"] = config.LogFile;
    node["Paused"] = config.Paused;
    node["LogMaxBytes"] = config.LogMaxBytes;
    node["BrightnessRetryCount"] = config.BrightnessRetryCount;
    node["BrightnessRetryDelayMs"] = config.BrightnessRetryDelayMs;
    node["NotifyOnPlanChange"] = config.NotifyOnPlanChange;
    node["HeartbeatIntervalMin"] = config.HeartbeatIntervalMin;
    node["AutoStartEnabled"] = config.AutoStartEnabled;
  }

  private void WriteNode(JsonObject node)
  {
    var dir = Path.GetDirectoryName(configPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    File.WriteAllText(configPath, node.ToJsonString(WriteOptions));
  }
}
