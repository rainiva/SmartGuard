using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class StatusReaderOperationIdTests
{
  [Fact]
  public void Read_sets_operation_id_from_status_json()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardOpIdReader_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.status.json");
    var payload = new StatusPayload
    {
      currentPlan = "平衡",
      operationId = "op-settings-cross-456",
    };
    File.WriteAllText(path, JsonSerializer.Serialize(payload));

    try
    {
      OperationIdContext.Clear();
      StatusReader.Read(path);
      OperationIdContext.Current.Should().Be("op-settings-cross-456");
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
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuardOpIdReader_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "SmartGuard.status.json");

    try
    {
      OperationIdContext.Set("op-previous");
      StatusReader.Read(path);
      OperationIdContext.Current.Should().BeNull();
    }
    finally
    {
      OperationIdContext.Clear();
      Directory.Delete(dir, recursive: true);
    }
  }
}
