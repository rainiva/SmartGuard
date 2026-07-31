using SmartGuard.Configuration;
using SmartGuard.Configuration.Shell;
using SmartGuard.Contracts;
using SmartGuard.Tray.Toast;
using System.Runtime.InteropServices;

namespace SmartGuard.Tray;

internal static class Program
{
  [STAThread]
  private static void Main(string[] args)
  {
    SetCurrentProcessExplicitAppUserModelID(SmartGuardToastAppId.AppId);
    ApplicationConfiguration.Initialize();
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

    var root = InstallRootResolver.Resolve(null, args);
    GlobalExceptionHandlers.Register("Tray", root);
    System.Windows.Forms.Application.ThreadException += (_, e) =>
      CrashLogWriter.Write("Tray", SmartGuardPaths.ResolveLogFile(
        new GuardConfigRepository(SmartGuardPaths.ConfigFile(root)).LoadOrDefault(root), root), e.Exception);
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

  [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
  private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
}
