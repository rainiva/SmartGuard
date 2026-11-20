using SmartGuard.Configuration;
using SmartGuard.Contracts;

namespace SmartGuard.Settings;

internal static class StatusReader
{
  public static StatusPayload? Read(string statusPath)
  {
    var status = StatusJsonReader.TryRead(statusPath);
    if (!string.IsNullOrEmpty(status?.operationId))
      OperationIdContext.Set(status!.operationId!);
    else
      OperationIdContext.Clear();
    return status;
  }
}
