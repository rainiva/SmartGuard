using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class StatusStoreOperationIdTests
{
  [Fact]
  public void Read_sets_operation_id_from_status_json()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardOpIdStore_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.status.json");
    var payload = new StatusPayload
    {
      currentPlan = "平衡",
      operationId = "op-cross-proc-123",
    };
    File.WriteAllText(path, JsonSerializer.Serialize(payload));

    try
    {
      OperationIdContext.Clear();
      var store = new StatusStore(path);
      store.Read();
      OperationIdContext.Current.Should().Be("op-cross-proc-123");
    }
    finally
    {
      OperationIdContext.Clear();
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Read_clears_operation_id_when_status_missing()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardOpIdStore_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.status.json");

    try
    {
      OperationIdContext.Set("op-previous");
      var store = new StatusStore(path);
      store.Read();
      OperationIdContext.Current.Should().BeNull();
    }
    finally
    {
      OperationIdContext.Clear();
      Directory.Delete(dir, recursive: true);
    }
  }
}
