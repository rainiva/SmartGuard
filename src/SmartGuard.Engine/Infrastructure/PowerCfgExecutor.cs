using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGuard.Engine.Infrastructure;

public static partial class PowerCfgExecutor
{
  private static readonly Regex GuidRegex = ActiveSchemeGuidPattern();
  private static readonly Encoding PowerCfgConsoleEncoding = CreatePowerCfgConsoleEncoding();

  public static Guid? ParseActiveSchemeGuid(string output)
  {
    var match = GuidRegex.Match(output);
    return match.Success ? Guid.Parse(match.Groups[1].Value) : null;
  }

  public static PowerSchemeInfo? ParseCurrentPlanInfo(string output)
  {
    foreach (Match match in PowerSchemeListPattern().Matches(output))
    {
      return new PowerSchemeInfo(
        Guid.Parse(match.Groups[1].Value),
        match.Groups[2].Value.Trim());
    }

    var guid = ParseActiveSchemeGuid(output);
    return guid is null ? null : new PowerSchemeInfo(guid.Value, null);
  }

  public static PowerSchemeInfo? GetCurrentPlanInfo()
  {
    var output = RunPowerCfg("/getactivescheme");
    return ParseCurrentPlanInfo(output);
  }

  public static Guid? GetCurrentPlanGuid() => GetCurrentPlanInfo()?.Guid;

  public static IReadOnlyDictionary<Guid, string> LoadPowerPlanCatalog()
  {
    return ParsePowerSchemeList(RunPowerCfg("/list"));
  }

  public static Dictionary<Guid, string> ParsePowerSchemeList(string output)
  {
    var result = new Dictionary<Guid, string>();
    foreach (Match match in PowerSchemeListPattern().Matches(output))
    {
      var guid = Guid.Parse(match.Groups[1].Value);
      var name = match.Groups[2].Value.Trim();
      result[guid] = name;
    }
    return result;
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
    return RunPowerCfg(arguments, TimeSpan.FromSeconds(5));
  }

  private static string RunPowerCfg(string arguments, TimeSpan timeout)
  {
    try
    {
      return RunPowerCfgAsync(arguments, timeout).GetAwaiter().GetResult();
    }
    catch (TimeoutException)
    {
      throw;
    }
    catch
    {
      return string.Empty;
    }
  }

  private static async Task<string> RunPowerCfgAsync(string arguments, TimeSpan timeout)
  {
    var psi = new ProcessStartInfo("powercfg.exe", arguments)
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      StandardOutputEncoding = PowerCfgConsoleEncoding,
      StandardErrorEncoding = PowerCfgConsoleEncoding,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    using var proc = Process.Start(psi);
    if (proc is null) return string.Empty;

    using var cts = new CancellationTokenSource(timeout);
    var readOutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
    var readErrTask = proc.StandardError.ReadToEndAsync(cts.Token);

    try
    {
      await proc.WaitForExitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
      try { proc.Kill(); } catch { }
      throw new TimeoutException($"powercfg.exe {arguments} timed out after {timeout.TotalSeconds}s");
    }

    var output = await readOutTask;
    var error = await readErrTask;
    return output + error;
  }

  private static Encoding CreatePowerCfgConsoleEncoding()
  {
    try
    {
      Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
      var codePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
      if (codePage > 0) return Encoding.GetEncoding(codePage);
    }
    catch
    {
      // fall back below
    }

    return Encoding.UTF8;
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

  [GeneratedRegex(@"GUID:\s+([0-9a-fA-F-]{36})\s+\(([^)]+)\)", RegexOptions.IgnoreCase)]
  private static partial Regex PowerSchemeListPattern();
}
