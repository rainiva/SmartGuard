using System.Text;
using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class PowerSchemeApiArchitectureTests
{
  [Fact]
  public void Engine_never_starts_powercfg()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src/SmartGuard.Engine"))
    {
      var source = File.ReadAllText(file, Encoding.UTF8);
      source.Should().NotContainEquivalentOf("\"powercfg", $"{file} must use PowerSchemeService (PWR-INV-SCHEME-API)");
      source.Should().NotContain("RunPowerCfg", $"{file} must not reach the powercfg runner");
      source.Should().NotContain("\"/setactive").And.NotContain("\"/getactivescheme");
    }
  }

  [Fact]
  public void Power_scheme_functions_are_imported_only_by_PowrProfNative()
  {
    string[] functions =
    [
      "PowerGetActiveScheme", "PowerSetActiveScheme", "PowerReadFriendlyName", "PowerEnumerate",
      "PowerReadACValueIndex", "PowerReadDCValueIndex", "PowerWriteACValueIndex", "PowerWriteDCValueIndex",
    ];
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      if (Path.GetFileName(file).Equals("PowrProfNative.cs", StringComparison.OrdinalIgnoreCase)) continue;
      var source = File.ReadAllText(file, Encoding.UTF8);
      foreach (var function in functions)
        source.Should().NotContain(function + "(", $"{file} imports a power scheme function outside PowrProfNative");
    }
  }
}
