using System.Globalization;
using System.Management;

namespace SmartGuard.Engine.Infrastructure;

// Only this short-lived child owns WMI/COM. The parent can terminate providers
// that ignore the enumeration timeout without stranding the guardian thread.
internal static class WmiProbeCommand
{
  private static readonly TimeSpan WmiTimeout = TimeSpan.FromMilliseconds(250);
  internal static bool TryRun(string[] args, out int exitCode)
  {
    exitCode = 0;
    if (args.Length == 0 || args[0] != "--wmi-probe") return false;
    if (!ValidArguments(args))
    {
      Console.WriteLine("SGWMI1|error|invalid-arguments");
      exitCode = 2;
      return true;
    }
    // The child must also expire if its parent crashes while COM is blocked.
    // This is confined to the internal child command, before any physical WMI.
    using var deadline = StartDeadline(Environment.Exit, TimeSpan.FromSeconds(3));
    try
    {
      var reply = args[1] switch
      {
        "brightness-read" => ReadBrightness(),
        "brightness-write" => WriteBrightness(int.Parse(args[2], CultureInfo.InvariantCulture)),
        _ => ReadBattery(),
      };
      Console.WriteLine(reply);
      if (reply.StartsWith("SGWMI1|error|", StringComparison.Ordinal)) exitCode = 3;
    }
    catch (ManagementException) { Console.WriteLine("SGWMI1|error|wmi-failed"); exitCode = 3; }
    catch { Console.WriteLine("SGWMI1|error|provider-failed"); exitCode = 3; }
    return true;
  }

  internal static IDisposable StartDeadline(Action<int> exit, TimeSpan budget)
    => new Timer(_ => exit(124), null, budget, Timeout.InfiniteTimeSpan);

  private static bool ValidArguments(string[] args)
    => (args.Length == 2 && args[1] is "brightness-read" or "battery-read") ||
       (args.Length == 3 && args[1] == "brightness-write" &&
        int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level is >= 0 and <= 100);

  private static ManagementObjectSearcher CreateQuery(string scope, string query)
    => new(scope, query, new System.Management.EnumerationOptions
    {
      ReturnImmediately = true,
      Timeout = WmiTimeout,
      Rewindable = false,
    });

  private static string ReadBrightness()
  {
    using var searcher = CreateQuery(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
    using var results = searcher.Get();
    foreach (ManagementObject item in results)
    {
      using (item)
        return "SGWMI1|brightness|" + Convert.ToInt32(item["CurrentBrightness"]).ToString(CultureInfo.InvariantCulture);
    }
    return "SGWMI1|error|unavailable";
  }

  private static string WriteBrightness(int level)
  {
    using var searcher = CreateQuery(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
    using var results = searcher.Get();
    foreach (ManagementObject item in results)
    {
      using (item)
      {
        var status = item.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)level });
        return Convert.ToUInt32(status) == 0 ? "SGWMI1|write|ok" : "SGWMI1|error|write-failed";
      }
    }
    return "SGWMI1|error|unavailable";
  }

  private static string ReadBattery()
  {
    using var searcher = CreateQuery(@"root\CIMV2", "SELECT EstimatedChargeRemaining, BatteryStatus, DesignCapacity FROM Win32_Battery");
    using var results = searcher.Get();
    var entries = new List<(int Percent, uint Weight)>();
    bool? onAc = null;
    // Provider input is bounded in both enumeration work and retained data.
    using var enumerator = results.GetEnumerator();
    while (entries.Count < 16 && enumerator.MoveNext())
    {
      using var battery = (ManagementObject)enumerator.Current;
      var percent = Convert.ToInt32(battery["EstimatedChargeRemaining"] ?? 0);
      var weight = Convert.ToUInt32(battery["DesignCapacity"] ?? 0);
      entries.Add((percent, weight == 0 ? 1 : weight));
      if (battery["BatteryStatus"] is { } raw)
      {
        var hint = BatteryStatusInterpreter.InterpretWmiBatteryStatus(Convert.ToInt32(raw));
        if (hint is true) onAc = true;
        else if (hint is false && onAc != true) onAc = false;
      }
    }
    if (entries.Count == 0) return "SGWMI1|error|unavailable";
    return "SGWMI1|battery|" + BatteryStatusInterpreter.AggregateEstimatedChargeRemaining(entries).ToString(CultureInfo.InvariantCulture)
      + "|" + (onAc is null ? "unknown" : onAc.Value ? "ac" : "battery");
  }
}
