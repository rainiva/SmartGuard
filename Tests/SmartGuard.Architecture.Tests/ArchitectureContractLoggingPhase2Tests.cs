using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractLoggingPhase2Tests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Contract_registers_log_level_filter_and_minimum_level()
  {
    ContractContent.Should().Contain("LogLevelFilter");
    ContractContent.Should().Contain("LogMinimumLevel");
  }

  [Fact]
  public void GuardianLoop_WriteLog_filters_by_minimum_level()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    source.Should().Contain("LogLevelFilter.ShouldWrite",
      "WriteLog 必须经统一级别过滤入口，禁止散落 level >= min 手写比较");
  }

  [Fact]
  public void Contract_registers_log_pipeline_family()
  {
    ContractContent.Should().Contain("LogPipeline");
    ContractContent.Should().Contain("LogPipelineOptions");
    ContractContent.Should().Contain("LogPipelineDiagnostics");
    ContractContent.Should().Contain("LogPriority");
  }
}
