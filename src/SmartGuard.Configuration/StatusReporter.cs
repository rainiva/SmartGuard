using System.Diagnostics;
using System.Text;

namespace SmartGuard.Configuration;

public static class StatusReporter
{
  public const int DefaultStartupLogTailLineCount = 8;

  public static string ReadStartupLogTail(string root, int lineCount = DefaultStartupLogTailLineCount)
  {
    var logPath = SmartGuardPaths.StartupLogFile(root);
    if (!File.Exists(logPath))
      return string.Empty;

    var lines = File.ReadAllLines(logPath);
    if (lines.Length <= lineCount)
      return string.Join(Environment.NewLine, lines);

    return string.Join(Environment.NewLine, lines[^lineCount..]);
  }

  public static string FormatStatus(string root, int startupLogTailLines = DefaultStartupLogTailLineCount)
  {
    var builder = new StringBuilder();
    builder.AppendLine("=== SmartGuard Status ===");
    builder.AppendLine();

    foreach (var taskName in ScheduledTaskRegistrar.TaskNames)
    {
      AppendTaskStatus(builder, taskName);
    }

    builder.AppendLine();
    var tail = ReadStartupLogTail(root, startupLogTailLines);
    if (!string.IsNullOrEmpty(tail))
    {
      builder.AppendLine("--- Last startup log ---");
      builder.AppendLine(tail);
    }

    return builder.ToString();
  }

  private static void AppendTaskStatus(StringBuilder builder, string taskName)
  {
    try
    {
      using var process = Process.Start(new ProcessStartInfo
      {
        FileName = "schtasks.exe",
        Arguments = $"/Query /TN \"{taskName}\" /FO LIST",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = true,
      });
      if (process is null)
        return;

      var output = process.StandardOutput.ReadToEnd();
      process.WaitForExit();
      foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
      {
        if (line.Contains("TaskName", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Status", StringComparison.OrdinalIgnoreCase))
        {
          builder.AppendLine(line);
        }
      }
    }
    catch
    {
      // task may be absent
    }
  }
}
