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

  [Fact]
  public void Contract_registers_log_query_service()
  {
    ContractContent.Should().Contain("LogQueryService");
    ContractContent.Should().Contain("LogIndexBuilder");
  }

  [Fact]
  public void LogViewController_has_structured_filter_fields()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogViewController.cs");
    source.Should().Contain("OperationIdFilter", "LogViewController 必须支持 OperationId 筛选");
    source.Should().Contain("EventNameFilter");
    source.Should().Contain("ProcessIdFilter");
    source.Should().Contain("ThreadIdFilter");
  }

  [Fact]
  public void WIP_unlock_verifies_four_ui_files_unlocked()
  {
    // S3 解锁的 4 个文件不再在 WIP 禁触清单中
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogViewController.cs");
    source.Should().Contain("OperationIdFilter",
      "LogViewController 已解锁并新增筛选字段");
  }

  [Fact]
  public void Contract_registers_log_query_filter_spec_placeholder()
  {
    // 预留 S6 的 LogQueryFilterSpec 登记位
    ContractContent.Should().Contain("LOG-P4-UI-FILTER");
  }

  [Fact]
  public void LogViewBackgroundQuery_exists_and_is_async()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogView/LogViewBackgroundQuery.cs");
    source.Should().Contain("QueryAsync");
    source.Should().Contain("CancellationToken");
    source.Should().Contain("Task<");
  }

  [Fact]
  public void SettingsLogPageHost_wires_background_query()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.cs");
    source.Should().Contain("LogViewBackgroundQuery",
      "SettingsLogPageHost 必须接线 LogViewBackgroundQuery 实现非阻塞查询（D4 裁决）");
  }
}
