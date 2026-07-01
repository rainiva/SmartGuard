using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Tray;

public sealed class StatusStore(string statusPath)
{
  private StatusPayload? _cached;
  private DateTime _cachedWriteTimeUtc = DateTime.MinValue;

  internal int DiskReadCountForTests { get; private set; }

  internal void ResetMetricsForTests()
  {
    DiskReadCountForTests = 0;
    _cached = null;
    _cachedWriteTimeUtc = DateTime.MinValue;
  }

  public StatusPayload? Read()
  {
    if (!File.Exists(statusPath))
    {
      _cached = null;
      _cachedWriteTimeUtc = DateTime.MinValue;
      return null;
    }

    var writeTimeUtc = File.GetLastWriteTimeUtc(statusPath);
    if (_cached is not null && _cachedWriteTimeUtc == writeTimeUtc)
      return _cached;

    DiskReadCountForTests++;
    _cached = StatusJsonReader.TryRead(statusPath);
    _cachedWriteTimeUtc = _cached is null ? DateTime.MinValue : writeTimeUtc;
    return _cached;
  }
}
