using System.Text.Json;
using SmartGuard.Contracts;
using SmartGuard.Tray;

namespace SmartGuard.Tray.Tests;

public class TrayReadinessTests
{
  [Fact]
  public void Status_line_not_waiting_when_status_file_in_install_root()
  {
    var installRoot = Path.Combine(Path.GetTempPath(), "sg-tray-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(installRoot);
    var statusPath = Path.Combine(installRoot, "SmartGuard.status.json");
    try
    {
      var payload = new StatusPayload
      {
        currentPlan = "平衡",
        batteryPercent = 88,
        isOnAC = true,
        brightness = 60,
        paused = false,
      };
      File.WriteAllText(statusPath, JsonSerializer.Serialize(payload));

      var status = new StatusStore(statusPath).Read();
      TrayStatusFormatter.FormatStatusLine(status).Should().NotContain("等待核心服务");
      TrayStatusFormatter.FormatTooltip(status).Should().NotContain("等待核心服务");
    }
    finally
    {
      if (Directory.Exists(installRoot))
        Directory.Delete(installRoot, true);
    }
  }
}
