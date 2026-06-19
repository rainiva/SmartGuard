using System.Diagnostics;

namespace SmartGuard.Configuration;

public sealed class PowerPlanActivator : IPowerPlanActivator
{
  public void SetActivePlan(Guid planGuid)
  {
    var psi = new ProcessStartInfo("powercfg.exe", $"/setactive {planGuid:D}")
    {
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    using var proc = Process.Start(psi);
    proc?.WaitForExit();
  }
}
