using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Tray;

public sealed class StatusStore(string statusPath)
{
  private StatusPayload? _cached;
  private FileReadStamp? _cachedStamp;
  private bool _cachedSettled;

  internal int DiskReadCountForTests { get; private set; }

  internal void ResetMetricsForTests()
  {
    DiskReadCountForTests = 0;
    Forget();
  }

  public StatusPayload? Read()
  {
    // Captured before the content is read; only a settled stamp is trusted (FileReadStamp).
    if (FileReadStamp.TryCapture(statusPath) is not { } stamp)
    {
      Forget();
      SyncOperationId(_cached);
      return null;
    }

    if (_cached is not null && _cachedSettled && _cachedStamp == stamp)
    {
      SyncOperationId(_cached);
      return _cached;
    }

    DiskReadCountForTests++;
    _cached = StatusJsonReader.TryRead(statusPath);
    _cachedStamp = _cached is null ? null : stamp;
    _cachedSettled = _cached is not null && stamp.IsSettled(DateTime.UtcNow);
    SyncOperationId(_cached);
    return _cached;
  }

  private void Forget()
  {
    _cached = null;
    _cachedStamp = null;
    _cachedSettled = false;
  }

  private static void SyncOperationId(StatusPayload? status)
  {
    if (!string.IsNullOrEmpty(status?.operationId))
      OperationIdContext.Set(status!.operationId!);
    else
      OperationIdContext.Clear();
  }
}
