using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartGuard.Contracts;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Infrastructure;

public sealed class StatusPublisher
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  private readonly string _statusPath;
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _maxStaleness;
  private StatusPayload? _lastPayload;
  private StatusPayload? _latestGuardPayload;
  private LogHealthStatus? _currentLogHealth;
  private long? _lastWriteTimestamp;

  public StatusPublisher(
    string statusPath,
    TimeProvider? timeProvider = null,
    TimeSpan? maxStaleness = null)
  {
    _statusPath = statusPath;
    _timeProvider = timeProvider ?? TimeProvider.System;
    _maxStaleness = maxStaleness ?? TimeSpan.FromSeconds(60);
    if (_maxStaleness <= TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(maxStaleness));
  }

  public void Publish(StatusPayload payload)
  {
    ArgumentNullException.ThrowIfNull(payload);
    _currentLogHealth = payload.logHealth ?? _currentLogHealth;
    var snapshot = Clone(payload);
    snapshot.logHealth = _currentLogHealth;
    _latestGuardPayload = snapshot;
    PublishCore(snapshot);
  }

  /// <summary>轻路径只更新健康自身时间，保留最后真实守护观测，不伪造守护新鲜度。</summary>
  public void PublishLogHealth(LogHealthStatus health)
  {
    _currentLogHealth = health;
    if (_latestGuardPayload is null) return;
    var snapshot = Clone(_latestGuardPayload);
    snapshot.logHealth = health;
    PublishCore(snapshot);
  }

  private void PublishCore(StatusPayload payload)
  {
    var now = _timeProvider.GetTimestamp();
    var meaningfulChange = _lastPayload is null
      || !StatusPayloadEquals(_lastPayload, payload)
      || IdleDroppedSignificantly(_lastPayload, payload);
    var freshnessDue = _lastWriteTimestamp is null
      || _timeProvider.GetElapsedTime(_lastWriteTimestamp.Value, now) >= _maxStaleness;

    if (!meaningfulChange && !freshnessDue)
      return;

    WritePayload(payload);
    _lastPayload = Clone(payload);
    _lastWriteTimestamp = now;
  }

  private void WritePayload(StatusPayload payload)
  {
    var dir = Path.GetDirectoryName(_statusPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    var temp = _statusPath + ".tmp";
    var backup = SmartGuardPaths.StatusBackupForFile(_statusPath);
    try
    {
      // Recover an interrupted replacement before attempting another write.
      if (!File.Exists(_statusPath) && File.Exists(backup))
        File.Move(backup, _statusPath);
      File.WriteAllText(
        temp,
        JsonSerializer.Serialize(payload, JsonOptions),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
      if (File.Exists(_statusPath))
        File.Replace(temp, _statusPath, destinationBackupFileName: backup);
      else
        File.Move(temp, _statusPath);
    }
    catch (Exception writeError)
    {
      try { if (File.Exists(temp)) File.Delete(temp); } catch { }
      try
      {
        if (!File.Exists(_statusPath) && File.Exists(backup))
          File.Move(backup, _statusPath);
      }
      catch (Exception recoveryError)
      {
        throw new AggregateException("Status replacement and recovery failed; recovery backup is retained.",
          writeError, recoveryError);
      }
      throw;
    }
  }

  private static bool IdleDroppedSignificantly(StatusPayload previous, StatusPayload current)
    => current.idleSeconds + 5 < previous.idleSeconds;

  private static bool StatusPayloadEquals(StatusPayload a, StatusPayload b)
  {
    return a.currentPlan == b.currentPlan
      && a.currentPlanGUID == b.currentPlanGUID
      && a.expectedPlan == b.expectedPlan
      && a.isOnAC == b.isOnAC
      && a.batteryPercent == b.batteryPercent
      && a.brightness == b.brightness
      && a.paused == b.paused
      && a.enginePid == b.enginePid
      && a.guardSource == b.guardSource
      && a.guardReason == b.guardReason
      && a.foregroundEventsAvailable == b.foregroundEventsAvailable
      && !HealthMeaningfullyChanged(a.logHealth, b.logHealth)
      && Equals(a.guardHealth, b.guardHealth)
      && a.notificationEvent?.id == b.notificationEvent?.id;
  }

  private static bool HealthMeaningfullyChanged(LogHealthStatus? previous, LogHealthStatus? current)
    => previous?.Code != current?.Code
      || (previous?.LostLines == 0 && current?.LostLines > 0)
      || (previous?.DroppedNormal == 0 && current?.DroppedNormal > 0)
      || (previous?.DroppedHigh == 0 && current?.DroppedHigh > 0);

  private static StatusPayload Clone(StatusPayload payload) => new()
  {
    timestamp = payload.timestamp, currentPlan = payload.currentPlan,
    currentPlanGUID = payload.currentPlanGUID, expectedPlan = payload.expectedPlan,
    idleSeconds = payload.idleSeconds, isOnAC = payload.isOnAC,
    batteryPercent = payload.batteryPercent, brightness = payload.brightness,
    paused = payload.paused, enginePid = payload.enginePid,
    lastExternalChange = payload.lastExternalChange, operationId = payload.operationId,
    guardSource = payload.guardSource, guardReason = payload.guardReason,
    foregroundEventsAvailable = payload.foregroundEventsAvailable, logHealth = payload.logHealth,
    guardHealth = payload.guardHealth,
    notificationEvent = payload.notificationEvent is null ? null : new NotificationEvent
    {
      id = payload.notificationEvent.id, kind = payload.notificationEvent.kind,
      title = payload.notificationEvent.title, body = payload.notificationEvent.body,
      at = payload.notificationEvent.at,
    },
  };
}
