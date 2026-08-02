using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractLoggingPhase3Tests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Contract_registers_crash_ring_buffer()
  {
    ContractContent.Should().Contain("CrashRingBuffer");
    ContractContent.Should().Contain("EmergencySyncWriter");
  }

  [Fact]
  public void Contract_registers_recovery_state_machine()
  {
    ContractContent.Should().Contain("LogRecoveryStateMachine");
    ContractContent.Should().Contain("LogConsumerRecovery");
  }

  [Fact]
  public void Contract_registers_deduplication_filter()
  {
    ContractContent.Should().Contain("LogDeduplicationFilter");
  }

  [Fact]
  public void Contract_registers_backpressure_controller()
  {
    ContractContent.Should().Contain("LogBackpressureController");
  }

  [Fact]
  public void Contract_registers_operation_id_context()
  {
    ContractContent.Should().Contain("OperationIdContext");
  }

  [Fact]
  public void Contract_registers_event_name_registry()
  {
    ContractContent.Should().Contain("EventNameRegistry");
    ContractContent.Should().Contain("EventNameMigration");
  }

  [Fact]
  public void GuardianLoop_WriteLog_uses_registered_event_names()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    source.Should().Contain("EventNameRegistry.",
      "WriteLog 调用点必须使用 EventNameRegistry 常量，禁止硬编码事件名");
  }

  [Fact]
  public void Contract_registers_health_and_injector()
  {
    ContractContent.Should().Contain("LogPipelineHealth");
    ContractContent.Should().Contain("LogFailureInjector");
    ContractContent.Should().Contain("LogHealthSnapshot");
  }

  [Fact]
  public void GuardianLoop_WriteBatch_uses_consumer_recovery()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs");
    source.Should().Contain("ExecuteWithRecovery",
      "管线消费回调必须经 LogConsumerRecovery.ExecuteWithRecovery 包装，否则消费恢复状态机未接入生产路径");
  }

  [Fact]
  public void GlobalExceptionHandlers_flushes_crash_ring_before_crash_log()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GlobalExceptionHandlers.cs");
    source.Should().Contain("FlushToSync",
      "崩溃回调必须在 CrashLogWriter.Write 前 flush 环形缓冲，否则 CrashRingBuffer 未接入崩溃路径");
  }

  [Fact]
  public void GuardianIterationRunner_generates_operation_id_per_iteration()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs");
    source.Should().Contain("OperationIdContext.GenerateNew",
      "每次迭代入口必须生成新 OperationId，否则日志上下文后缀与 status.json 中继无源头赋值");
  }

  [Fact]
  public void GuardianIterationRunner_generates_trace_id_per_iteration()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs");
    source.Should().Contain("TraceIdContext.GenerateNew",
      "每次迭代入口必须生成新 TraceId，否则结构化日志 trace 后缀无源头赋值");
  }
}
