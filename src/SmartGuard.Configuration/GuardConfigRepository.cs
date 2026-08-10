using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;

namespace SmartGuard.Configuration;

public sealed class GuardConfigRepository
{
  private readonly string _configPath;
  private static readonly JsonSerializerOptions WriteOptions = new()
  {
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };
  private GuardConfig? _readCache;
  private DateTime _readCacheWriteTimeUtc = DateTime.MinValue;

  public GuardConfigRepository(string configPath)
  {
    _configPath = configPath;
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
      GuardConfig? result = _readCache;
      if (result is not null && node is JsonObject obj)
      {
        ApplyNotificationMigration(result, obj);
        ApplyThemeMigration(result, obj);
        ApplyLogViewPreferencesMigration(result, obj);
        if (RemoveRetiredSecretFields(obj))
          TryPersistRetiredFieldCleanup(obj);
      }
      _readCache = result;
      _readCacheWriteTimeUtc = File.GetLastWriteTimeUtc(_configPath);
      return result;
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

  internal static void ApplyLogViewPreferencesMigration(GuardConfig config, JsonObject node)
  {
    if (!node.ContainsKey("LogViewPreferences") || config.LogViewPreferences is null)
      config.LogViewPreferences = new LogViewPreferences();
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

  public void AppendInfoLog(string message, string root, string? fallbackLogPath = null)
  {
    var config = LoadOrDefault(root);
    var logPath = SmartGuardPaths.ResolveLogFile(config, root);
    if (string.IsNullOrWhiteSpace(logPath))
      logPath = fallbackLogPath ?? SmartGuardPaths.DefaultLogFile(root);
    var logEvent = LogStructuredSink.CreateComponentEvent(
      LogSeverity.Info,
      LogComponentRegistry.Configuration,
      LogChannelRegistry.Policy,
      EventNameRegistry.ConfigMutationLogged,
      message);
    var retention = LogRetentionPolicy.FromGuardConfig(config);
    if (!LogStructuredSink.TryWriteBatch(logPath, new[] { logEvent }, retention))
    {
      LogFallbackChain.WriteEvent(
        () => false,
        fallbackLogPath ?? SmartGuardPaths.StartupLogFile(root),
        logEvent);
    }
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
    node["LogMinimumLevel"] = config.LogMinimumLevel;
    node["LogHeartMinIntervalSec"] = config.LogHeartMinIntervalSec;
    node["LogRetentionDays"] = config.LogRetentionDays;
    node["LogMaxArchiveFiles"] = config.LogMaxArchiveFiles;
    node["LogDirectoryMaxBytes"] = config.LogDirectoryMaxBytes;
    node["BrightnessRetryCount"] = config.BrightnessRetryCount;
    node["BrightnessRetryDelayMs"] = config.BrightnessRetryDelayMs;
    node["NotifyOnPlanChange"] = config.NotifyOnPlanChange;
    node["NotifyOnExternalChange"] = config.NotifyOnExternalChange;
    node["HeartbeatIntervalMin"] = config.HeartbeatIntervalMin;
    node["AutoStartEnabled"] = config.AutoStartEnabled;
    node["ThemeFollowSystem"] = config.ThemeFollowSystem;
    node["ThemeIsDark"] = config.ThemeIsDark;
    ApplyLogViewPreferences(node, config.LogViewPreferences);
    RemoveRetiredSecretFields(node);
    if (config.ManualHighPerformanceUntil is { } until)
      node["ManualHighPerformanceUntil"] = until.ToString("o");
    else
      node.Remove("ManualHighPerformanceUntil");
  }

  private static void ApplyLogViewPreferences(JsonObject node, LogViewPreferences preferences)
  {
    JsonObject prefsNode;
    if (node["LogViewPreferences"] is JsonObject existing)
      prefsNode = existing;
    else
    {
      prefsNode = new JsonObject();
      node["LogViewPreferences"] = prefsNode;
    }

    if (preferences.DefaultFilterId is null)
      prefsNode.Remove("DefaultFilterId");
    else
      prefsNode["DefaultFilterId"] = preferences.DefaultFilterId;

    prefsNode["SavedFilters"] = JsonSerializer.SerializeToNode(
      preferences.SavedFilters,
      GuardConfig.JsonOptions) ?? new JsonArray();
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

  private static bool RemoveRetiredSecretFields(JsonObject node)
    => RetiredSecretFieldRegistry.RemoveFrom(node);

  private void TryPersistRetiredFieldCleanup(JsonObject node)
  {
    try
    {
      WriteNodeWithoutCacheInvalidation(node.ToJsonString(WriteOptions));
    }
    catch
    {
      // Loading valid policy must not fail only because a retired field cannot be cleaned up yet.
    }
  }

  private void WriteNodeWithoutCacheInvalidation(string content)
  {
    var dir = Path.GetDirectoryName(_configPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    GuardConfigAtomicFileWriter.WriteAllText(_configPath, content);
  }
}
