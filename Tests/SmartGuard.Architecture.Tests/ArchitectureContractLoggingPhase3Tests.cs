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
  public void Contract_registers_explicit_log_event_outcome()
  {
    ContractContent.Should().Contain("LogEventOutcome");
    ContractContent.Should().Contain("禁止从 Severity 或 Message 推导状态");
  }

  [Fact]
  public void Default_log_table_allocates_no_resident_column_for_sparse_outcomes()
  {
    var settingsXaml = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SmartGuard.Settings.xaml");
    var logPageStart = settingsXaml.IndexOf("<!-- Logs Page -->", StringComparison.Ordinal);
    logPageStart.Should().BeGreaterThanOrEqualTo(0, "the Logs Page marker should exist in Settings XAML");
    var logPage = settingsXaml[logPageStart..];

    logPage.Should().NotContain("Header=\"状态\"",
      "默认日志表不为稀疏 outcome 分配常驻列");
    logPage.Should().NotContain("Binding=\"{Binding StatusText}\"");
    logPage.Should().Contain("按级别、时间、来源和事件查看当前页日志");

    var tableRow = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogTableRow.cs");
    tableRow.Should().NotContain("StatusText",
      "行投影不保留状态文本，结构化 outcome 仍保留在 LogEvent 与 JSONL 诊断数据中");
    tableRow.Should().Contain("SourceEvent");

    ContractContent.Should().Contain("生产端显式写已登记 outcome");
    ContractContent.Should().Contain("默认表格不分配常驻列");
    ContractContent.Should().Contain("结构化 outcome 保留在 `LogEvent` 与 JSONL");
    ContractContent.Should().NotContain("缺失或未知值显示 `—`");
  }

  [Fact]
  public void GuardianLoop_WriteLog_uses_registered_event_names()
  {
    var source = SourceScanHelper.ReadGuardianLoopSource();
    source.Should().Contain("EventNameRegistry.",
      "WriteLog 调用点必须使用 EventNameRegistry 常量，禁止硬编码事件名");
  }

  [Fact]
  public void Contract_registers_health_and_injector()
  {
    ContractContent.Should().Contain("LogPipelineHealth");
    ContractContent.Should().Contain("LogFailureInjector");
    ContractContent.Should().Contain("LogHealthSnapshot");
    ContractContent.Should().Contain("[DIAG]");
    ContractContent.Should().Contain("标准时间戳");
  }

  [Fact]
  public void GuardianLoop_WriteBatch_uses_consumer_recovery()
  {
    var source = SourceScanHelper.ReadGuardianLoopSource();
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

  [Fact]
  public void GuardianIterationRunner_assigns_operation_id_into_status_payload()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianIterationRunner.cs");
    source.Should().Contain("operationId = OperationIdContext.Current",
      "StatusPayload 必须把当前 OperationId 写入 operationId 字段，否则跨进程日志中继丢失源头，且仅生成不赋值门禁无法拦截");
  }

  [Fact]
  public void GuardianLoop_enables_pipeline_health_reporting()
  {
    var source = SourceScanHelper.ReadGuardianLoopSource();
    source.Should().Contain("StartHealthReporting",
      "GuardianLoop 启动管线后必须调 StartHealthReporting，否则 LogPipelineHealth 定时落盘未在生产启用");
  }

  [Fact]
  public void LogPipeline_health_reporting_uses_structured_health_event()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/LogPipeline.cs");
    source.Should().Contain("CreateHealthEvent",
      "健康记录必须经结构化 LogEvent 入队，不能再以裸 JSON 字符串混入主日志");
  }

  [Fact]
  public void GuardianLoop_writes_to_crash_ring_buffer()
  {
    var source = SourceScanHelper.ReadGuardianLoopSource();
    source.Should().Contain("CrashRingBuffer.Instance",
      "高优先级日志行必须同步 Add 到 CrashRingBuffer.Instance，否则崩溃现场环形缓冲未接入生产路径");
  }
}
