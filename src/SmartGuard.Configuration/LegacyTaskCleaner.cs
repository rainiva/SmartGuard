using System.Diagnostics;
using System.Management;

namespace SmartGuard.Configuration;

public static class LegacyTaskCleaner
{
    private static readonly string[] LegacyTaskNames =
    {
        "SmartPowerPlan Guardian",
        "SmartPowerPlan Tray"
    };

    public static void CleanLegacyTasks()
    {
        StopLegacyProcesses();
        foreach (var name in LegacyTaskNames)
        {
            TryDeleteTask(name);
        }
    }

    private static void StopLegacyProcesses()
    {
        try
        {
            var query = $@"SELECT ProcessId, CommandLine FROM Win32_Process WHERE CommandLine LIKE '%{LegacyPaths.CommandLinePattern}%'";
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject obj in searcher.Get())
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                    // best-effort
                }
            }
        }
        catch
        {
            // WMI may be unavailable; ignore
        }
    }

    private static void TryDeleteTask(string name)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /TN \"{name}\" /F",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
        }
        catch
        {
            // task may already be absent
        }
    }
}
