using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SmartGuard.Engine.Infrastructure;

public static partial class PowerCfgExecutor
{
  private static readonly Regex GuidRegex = ActiveSchemeGuidPattern();

  public static Guid? ParseActiveSchemeGuid(string output)
  {
    var match = GuidRegex.Match(output);
    return match.Success ? Guid.Parse(match.Groups[1].Value) : null;
  }

  public static Guid? GetCurrentPlanGuid()
  {
    var output = RunPowerCfg("/getactivescheme");
    return ParseActiveSchemeGuid(output);
  }

  public static void SetActivePlan(Guid planGuid)
  {
    RunPowerCfg($"/setactive {planGuid:D}");
  }

  public static bool IsBrightnessSupported(Guid probePlanGuid)
  {
    var output = RunPowerCfg($"/setacvalueindex {probePlanGuid:D} SUB_VIDEO VIDEONORMALLEVEL 50");
    return !ContainsPowerCfgError(output);
  }

  public static void SyncPlanBrightness(Guid planGuid, int brightness)
  {
    if (brightness < 0 || brightness > 100) return;
    RunPowerCfg($"/setacvalueindex {planGuid:D} SUB_VIDEO VIDEONORMALLEVEL {brightness}");
    RunPowerCfg($"/setdcvalueindex {planGuid:D} SUB_VIDEO VIDEONORMALLEVEL {brightness}");
  }

  public static void DisableAdaptiveBrightness(Guid planGuid)
  {
    RunPowerCfg($"/setacvalueindex {planGuid:D} SUB_VIDEO ADAPTBRIGHT 0");
    RunPowerCfg($"/setdcvalueindex {planGuid:D} SUB_VIDEO ADAPTBRIGHT 0");
  }

  private static string RunPowerCfg(string arguments)
  {
    var psi = new ProcessStartInfo("powercfg.exe", arguments)
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    using var proc = Process.Start(psi);
    if (proc is null) return string.Empty;
    var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
    proc.WaitForExit();
    return output;
  }

  private static bool ContainsPowerCfgError(string text)
  {
    return text.Contains("不存在", StringComparison.Ordinal)
      || text.Contains("无效", StringComparison.Ordinal)
      || text.Contains("invalid", StringComparison.OrdinalIgnoreCase)
      || text.Contains("not exist", StringComparison.OrdinalIgnoreCase);
  }

  [GeneratedRegex(@"GUID:\s+([0-9a-fA-F-]{36})", RegexOptions.IgnoreCase)]
  private static partial Regex ActiveSchemeGuidPattern();
}
