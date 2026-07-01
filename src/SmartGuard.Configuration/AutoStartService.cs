using System.Diagnostics;

namespace SmartGuard.Configuration;

public static class AutoStartService
{
  public static IReadOnlyList<string> ScheduledTaskNames => ScheduledTaskRegistrar.TaskNames;

  public static bool NeedsUpdate(bool enabled, bool? previousEnabled)
  {
    if (previousEnabled is null) return true;
    return enabled != previousEnabled.Value;
  }

  public static bool SyncFromTasks()
  {
    foreach (var name in ScheduledTaskNames)
    {
      if (!TryGetTaskState(name, out var state))
        return false;
      if (string.Equals(state, "Disabled", StringComparison.OrdinalIgnoreCase))
        return false;
    }

    return true;
  }

  public static void SetEnabled(bool enabled, string root)
  {
    foreach (var name in ScheduledTaskNames)
    {
      if (TryGetTaskState(name, out var state))
      {
        if (enabled)
        {
          if (string.Equals(state, "Disabled", StringComparison.OrdinalIgnoreCase))
            RunSchTasks($"/Change /TN \"{name}\" /ENABLE");
        }
        else if (!string.Equals(state, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
          RunSchTasks($"/Change /TN \"{name}\" /DISABLE");
        }

        continue;
      }

      if (!enabled) continue;
      ScheduledTaskRegistrar.RegisterIfMissing(name, root);
    }
  }

  private static bool TryGetTaskState(string taskName, out string? state)
  {
    state = null;
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
      if (process is null) return false;
      var output = process.StandardOutput.ReadToEnd();
      process.WaitForExit();
      if (process.ExitCode != 0) return false;
      foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
      {
        if (line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))
        {
          state = line["Status:".Length..].Trim();
          return true;
        }
      }

      return false;
    }
    catch
    {
      return false;
    }
  }

  private static void RunSchTasks(string arguments)
  {
    Process.Start(new ProcessStartInfo
    {
      FileName = "schtasks.exe",
      Arguments = arguments,
      UseShellExecute = false,
      CreateNoWindow = true,
    })?.WaitForExit();
  }
}
