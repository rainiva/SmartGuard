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
    var source = SourceScanHelper.ReadGuardianLoopSource();
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
    var source = SourceScanHelper.ReadGuardianLoopSource();
    source.Should().Contain("LogPipeline", "Engine 日志必须经异步管线落盘，业务线程不得同步写主日志");
    source.Should().Contain("TryEnqueue", "WriteLog 必须非阻塞入队");
    source.Should().Contain("StopAsync", "进程退出必须受控关闭管线，不得丢尾部日志");
  }

  [Fact]
  public void GuardianLoop_throttles_heart_logs_via_sampler()
  {
    var source = SourceScanHelper.ReadGuardianLoopSource();
    source.Should().Contain("HeartLogSampler", "Heart 心跳持久化必须经采样治理，防秒级心跳源刷爆日志");
    ContractContent.Should().Contain("HeartLogSampler");
    ContractContent.Should().Contain("LogHeartMinIntervalSec");
  }

  [Fact]
  public void Contract_registers_retention_limits_and_pipeline_enforces_them()
  {
    ContractContent.Should().Contain("LogMaxArchiveFiles");
    ContractContent.Should().Contain("LogDirectoryMaxBytes");
    var writer = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuardLogWriter.cs");
    writer.Should().Contain("EnforceArchiveLimits", "configured log 批量落盘必须在 writer 互斥区内执行归档容量治理");
    var loop = SourceScanHelper.ReadGuardianLoopSource();
    loop.Should().Contain("LogRetentionPolicy.FromGuardConfig",
      "Engine 批量落盘必须携带 retention policy 进入 SmartGuardLogWriter");
  }
}
