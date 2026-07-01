using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartGuard.Configuration;

public sealed class GuardConfigRepository
{
  private readonly string _configPath;
  private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
  private GuardConfig? _readCache;
  private DateTime _readCacheWriteTimeUtc = DateTime.MinValue;
  private readonly FileSystemWatcher? _configWatcher;

  public GuardConfigRepository(string configPath)
  {
    _configPath = configPath;
    _configWatcher = CreateConfigWatcher(configPath, InvalidateReadCache);
  }

  internal int DiskReadCountForTests { get; private set; }

  internal void ResetMetricsForTests() => DiskReadCountForTests = 0;

  public GuardConfig? TryLoad()
  {
    if (!File.Exists(_configPath))
    {
      InvalidateReadCache();
      return null;
    }

    var writeTimeUtc = File.GetLastWriteTimeUtc(_configPath);
    if (_readCache is not null && _readCacheWriteTimeUtc == writeTimeUtc)
      return _readCache;

    DiskReadCountForTests++;
    try
    {
      var node = JsonNode.Parse(File.ReadAllText(_configPath));
      if (node is null)
      {
        InvalidateReadCache();
        return null;
      }

      _readCache = node.Deserialize<GuardConfig>(GuardConfig.JsonOptions);
      if (_readCache is not null && node is JsonObject obj)
      {
        ApplyNotificationMigration(_readCache, obj);
        ApplyThemeMigration(_readCache, obj);
      }
      _readCacheWriteTimeUtc = writeTimeUtc;
      return _readCache;
    }
    catch
    {
      InvalidateReadCache();
      return null;
    }
  }

  public GuardConfig LoadOrDefault(string root)
  {
    var loaded = TryLoad();
    return loaded ?? GuardConfig.CreateDefault(root);
  }

  internal static void ApplyNotificationMigration(GuardConfig config, JsonObject node)
  {
    if (!node.ContainsKey("NotifyOnExternalChange"))
      config.NotifyOnExternalChange = config.NotifyOnPlanChange;
  }

  internal static void ApplyThemeMigration(GuardConfig config, JsonObject node)
  {
    if (!node.ContainsKey("ThemeFollowSystem"))
      config.ThemeFollowSystem = true;
    if (!node.ContainsKey("ThemeIsDark"))
      config.ThemeIsDark = false;
  }

  public void Save(GuardConfig config)
  {
    var node = LoadOrCreateNode();
    ApplyConfig(node, config);
    var content = node.ToJsonString(WriteOptions);

    // Idempotent save: do not touch the file if the serialized content is unchanged.
    if (File.Exists(_configPath) && File.ReadAllText(_configPath) == content)
      return;

    WriteNode(content);
  }

  public void SetManualHighPerformanceUntil(DateTime until)
  {
    var node = LoadOrCreateNode();
    node["ManualHighPerformanceUntil"] = until.ToString("o");
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
    if (!File.Exists(_configPath)) return new JsonObject();
    try
    {
      return JsonNode.Parse(File.ReadAllText(_configPath)) as JsonObject ?? new JsonObject();
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
    node["NotifyOnExternalChange"] = config.NotifyOnExternalChange;
    node["HeartbeatIntervalMin"] = config.HeartbeatIntervalMin;
    node["AutoStartEnabled"] = config.AutoStartEnabled;
    node["ThemeFollowSystem"] = config.ThemeFollowSystem;
    node["ThemeIsDark"] = config.ThemeIsDark;
    node["GitHubToken"] = config.GitHubToken;
    if (config.ManualHighPerformanceUntil is { } until)
      node["ManualHighPerformanceUntil"] = until.ToString("o");
    else
      node.Remove("ManualHighPerformanceUntil");
  }

  private void WriteNode(JsonObject node) => WriteNode(node.ToJsonString(WriteOptions));

  private void WriteNode(string content)
  {
    var dir = Path.GetDirectoryName(_configPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    GuardConfigAtomicFileWriter.WriteAllText(_configPath, content);
    InvalidateReadCache();
  }

  private void InvalidateReadCache()
  {
    _readCache = null;
    _readCacheWriteTimeUtc = DateTime.MinValue;
  }

  private static FileSystemWatcher? CreateConfigWatcher(string path, Action onChanged)
  {
    var dir = Path.GetDirectoryName(path);
    var fileName = Path.GetFileName(path);
    if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(fileName))
      return null;

    var watcher = new FileSystemWatcher(dir, fileName)
    {
      NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
    };
    FileSystemEventHandler handler = (_, _) => onChanged();
    watcher.Changed += handler;
    watcher.Created += handler;
    watcher.Deleted += handler;
    watcher.Renamed += (_, _) => onChanged();
    watcher.EnableRaisingEvents = true;
    return watcher;
  }
}
