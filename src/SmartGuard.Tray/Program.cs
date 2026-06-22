using SmartGuard.Contracts;
using SmartGuard.Tray.Toast;

namespace SmartGuard.Tray;

internal static class Program
{
  [STAThread]
  private static void Main(string[] args)
  {
    ApplicationConfiguration.Initialize();
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

    var root = RootResolver.Resolve(args);
    using var guard = SingleInstanceGuard.TryAcquire("Tray");
    if (!guard.IsOwner)
    {
      MessageBox.Show(
        "智能电源守护托盘已在运行。",
        "智能电源守护",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
      return;
    }

    Application.Run(new TrayApplicationContext(root));
  }
}
