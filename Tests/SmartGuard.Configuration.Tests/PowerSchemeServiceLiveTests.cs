using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("PowerPlanCatalogProvider")]
public class PowerSchemeServiceLiveTests
{
  [Fact]
  public async Task Active_scheme_matches_powercfg()
  {
    var api = await PowerSchemeService.GetActiveAsync();
    api.Succeeded.Should().BeTrue();
    var output = await RunPowerCfgOracleAsync("/getactivescheme");
    var match = Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
    match.Success.Should().BeTrue();
    api.Value.Should().Be(Guid.Parse(match.Groups[1].Value));
  }

  [Fact]
  public async Task Builtin_balanced_scheme_has_a_name_and_a_missing_scheme_reports_not_found()
  {
    var balanced = await PowerSchemeService.ReadNameAsync(PowerPlanCatalogProvider.BalancedPlanGuid);
    balanced.Succeeded.Should().BeTrue();
    balanced.Value.Should().NotBeNullOrWhiteSpace();
    var missing = await PowerSchemeService.ReadNameAsync(Guid.NewGuid());
    missing.Status.FailureKind.Should().Be("api-error");
    missing.Status.Win32Error.Should().Be(2);
  }

  [Fact]
  public async Task Enumerated_schemes_include_the_active_scheme()
  {
    var active = await PowerSchemeService.GetActiveAsync();
    var schemes = await PowerSchemeService.EnumerateSchemesAsync();
    schemes.Succeeded.Should().BeTrue();
    schemes.Value.Should().Contain(active.Value);
  }

  [Fact]
  public async Task Brightness_setting_read_is_supported_or_reports_not_found()
  {
    var read = await PowerSchemeService.ReadValueIndexAsync(PowerPlanCatalogProvider.BalancedPlanGuid,
      PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac);
    (read.Succeeded || read.Status.Win32Error == 2).Should().BeTrue($"unexpected status {read.Status}");
  }

  // Creates and deletes a temporary scheme; never touches the active scheme. User-executed.
  [PowerWriteFact]
  public async Task Writes_land_on_a_temporary_duplicate_scheme_only()
  {
    var activeBefore = (await PowerSchemeService.GetActiveAsync()).Value;
    var source = PowerPlanCatalogProvider.BalancedPlanGuid;
    var destination = IntPtr.Zero;
    PowerDuplicateScheme(IntPtr.Zero, ref source, ref destination).Should().Be(0u);
    var temporary = Marshal.PtrToStructure<Guid>(destination);
    LocalFree(destination);
    try
    {
      var write = await PowerSchemeService.WriteValueIndexAsync(temporary, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac, 37);
      write.Succeeded.Should().BeTrue();
      (await PowerSchemeService.ReadValueIndexAsync(temporary, PowerSchemeSetting.VideoNormalLevel, PowerSource.Ac)).Value.Should().Be(37u);
    }
    finally
    {
      PowerDeleteScheme(IntPtr.Zero, ref temporary).Should().Be(0u);
    }
    (await PowerSchemeService.GetActiveAsync()).Value.Should().Be(activeBefore);
  }

  // powercfg is used here only as an independent oracle; production code never starts it.
  // One budget covers reading the output and waiting for exit; a stuck oracle is killed, never awaited forever.
  private static async Task<string> RunPowerCfgOracleAsync(string arguments)
  {
    using var process = Process.Start(new ProcessStartInfo("powercfg.exe", arguments)
    { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    try
    {
      var output = await process.StandardOutput.ReadToEndAsync(budget.Token);
      await process.WaitForExitAsync(budget.Token);
      return output;
    }
    finally
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(2000);
      }
    }
  }

  [DllImport("powrprof.dll")] private static extern uint PowerDuplicateScheme(IntPtr root, ref Guid source, ref IntPtr destination);
  [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
  [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}

internal sealed class PowerWriteFactAttribute : FactAttribute
{
  public PowerWriteFactAttribute()
  {
    // The same variable also lifts PowerWriteGuard, which otherwise refuses every real write in this assembly.
    if (!PowerWriteGuard.WritesRequested)
      Skip = $"Creates and deletes a temporary power scheme; set {PowerWriteGuard.Variable}=1 to run (user-executed).";
  }
}
