using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractLoggingPhase4Tests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Contract_registers_trace_id_context()
  {
    ContractContent.Should().Contain("TraceIdContext");
    ContractContent.Should().Contain("SpanIdContext");
    ContractContent.Should().Contain("UserActionIdContext");
  }

  [Fact]
  public void GuardianLoop_WriteLog_passes_context_to_formatter()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    source.Should().Contain("TraceIdContext.Current",
      "WriteLog 必须传入 TraceIdContext.Current 给 LogLineFormatter.Format");
    source.Should().Contain("SpanIdContext.Current");
  }

  [Fact]
  public void LogLineFormatter_Format_supports_context_parameters()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/LogLineFormatter.cs");
    source.Should().Contain("traceId", "Format 必须支持 traceId 可选参数");
    source.Should().Contain("spanId");
    source.Should().Contain("operationId");
    source.Should().Contain("userActionId");
  }
}
