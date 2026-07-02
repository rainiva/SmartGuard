using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class ElevationHelper
{
  internal static Func<ProcessStartInfo, Process?>? StartProcessForTests;

  public static Process? StartElevated(string fileName, string arguments)
  {
    return StartProcess(new ProcessStartInfo
    {
      FileName = fileName,
      Arguments = arguments,
      Verb = "runas",
      UseShellExecute = true,
    });
  }

  public static Process? RerunCurrentProcessElevated(string arguments)
  {
    var exe = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(exe))
      return null;

    return StartElevated(exe, arguments);
  }

  private static Process? StartProcess(ProcessStartInfo startInfo)
  {
    if (StartProcessForTests is not null)
      return StartProcessForTests(startInfo);

    return Process.Start(startInfo);
  }
}
