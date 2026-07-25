using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class InnoStopDelegationArchitectureTests
{
  [Fact]
  public void Inno_TryStopSmartGuardProcesses_delegates_to_engine_uninstall()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var stopBlock = ExtractFunctionBody(iss, "TryStopSmartGuardProcesses");
    stopBlock.Should().Contain("--uninstall",
      "installer stop must delegate to EngineLifecycle via engine exe --uninstall");
    stopBlock.Should().NotContain("schtasks /End",
      "duplicate schtasks stop logic must live in EngineLifecycle only");
    stopBlock.Should().NotContain("taskkill.exe",
      "duplicate taskkill logic must live in EngineLifecycle only");
  }

  [Fact]
  public void Inno_TryStopSmartGuardProcesses_must_use_generated_engine_define_not_literal_exe_name()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var stopBlock = ExtractFunctionBody(iss, "TryStopSmartGuardProcesses");
    stopBlock.Should().Contain("--uninstall");
    stopBlock.Should().Contain(@"\bin\{#EngineExeName}");
    stopBlock.Should().NotContain("SmartGuard.Engine.exe");
  }

  private static string ExtractFunctionBody(string iss, string functionName)
  {
    var marker = $"function {functionName}(): Boolean;";
    var start = iss.IndexOf(marker, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1, $"function {functionName} must exist in SmartGuard.iss");
    var end = iss.IndexOf("\nfunction ", start + marker.Length, StringComparison.Ordinal);
    end.Should().BeGreaterThan(start);
    return iss[start..end];
  }
}
