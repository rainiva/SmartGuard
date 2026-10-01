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
    var now = _timeProvider.GetTimestamp();
    var meaningfulChange = _lastPayload is null
      || !StatusPayloadEquals(_lastPayload, payload)
      || IdleDroppedSignificantly(_lastPayload, payload);
    var freshnessDue = _lastWriteTimestamp is null
      || _timeProvider.GetElapsedTime(_lastWriteTimestamp.Value, now) >= _maxStaleness;

    if (!meaningfulChange && !freshnessDue)
      return;

    WritePayload(payload);
    _lastPayload = payload;
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
      && a.notificationEvent?.id == b.notificationEvent?.id;
  }
}
