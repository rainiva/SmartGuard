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

  [Fact]
  public void Contract_registers_pipeline_lifecycle_state_machine()
  {
    ContractContent.Should().Contain("LogPipelineState");
    ContractContent.Should().Contain("StopAsync");
  }

  [Fact]
  public void GuardianLoop_enqueues_logs_into_async_pipeline()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    source.Should().Contain("LogPipeline", "Engine 日志必须经异步管线落盘，业务线程不得同步写主日志");
    source.Should().Contain("TryEnqueue", "WriteLog 必须非阻塞入队");
    source.Should().Contain("StopAsync", "进程退出必须受控关闭管线，不得丢尾部日志");
  }

  [Fact]
  public void GuardianLoop_throttles_heart_logs_via_sampler()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    source.Should().Contain("HeartLogSampler", "Heart 心跳持久化必须经采样治理，防秒级心跳源刷爆日志");
    ContractContent.Should().Contain("HeartLogSampler");
    ContractContent.Should().Contain("LogHeartMinIntervalSec");
  }
}
