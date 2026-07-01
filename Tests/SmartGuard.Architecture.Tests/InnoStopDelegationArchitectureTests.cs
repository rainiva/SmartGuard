using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class InnoStopDelegationArchitectureTests
{
  [Fact]
  public void Inno_StopSmartGuardProcesses_delegates_to_engine_uninstall()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    var stopBlock = ExtractProcedureBody(iss, "StopSmartGuardProcesses");
    stopBlock.Should().Contain("--uninstall",
      "installer stop must delegate to EngineLifecycle via SmartGuard.Engine.exe --uninstall");
    stopBlock.Should().Contain("SmartGuard.Engine.exe");
    stopBlock.Should().NotContain("schtasks /End",
      "duplicate schtasks stop logic must live in EngineLifecycle only");
    stopBlock.Should().NotContain("taskkill.exe",
      "duplicate taskkill logic must live in EngineLifecycle only");
  }

  private static string ExtractProcedureBody(string iss, string procedureName)
  {
    var marker = $"procedure {procedureName}();";
    var start = iss.IndexOf(marker, StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1, $"procedure {procedureName} must exist in SmartGuard.iss");
    var end = iss.IndexOf("end;", start, StringComparison.Ordinal);
    end.Should().BeGreaterThan(start);
    return iss[start..(end + 4)];
  }
}
