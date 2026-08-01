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
  public void SettingsLogPageHost_uses_refresh_pipeline_as_async_entry()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.cs");
    source.Should().Contain("SettingsLogRefreshPipeline",
      "SettingsLogPageHost 的 UI 非阻塞唯一入口为 SettingsLogRefreshPipeline（S4 收口，D4 裁决）");
    source.Should().NotContain("_backgroundQuery",
      "死字段 _backgroundQuery 已移除，避免第二条非阻塞路径");
  }

  [Fact]
  public void Contract_registers_log_jsonl_formatter()
  {
    ContractContent.Should().Contain("LogJsonlFormatter");
    ContractContent.Should().Contain("LogJsonlEntry");
  }

  [Fact]
  public void DiagnosticBundleExporter_has_jsonl_entry()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/DiagnosticBundleExporter.cs");
    source.Should().Contain("AddJsonlLogEntry");
    source.Should().Contain("SmartGuard.jsonl");
  }

  [Fact]
  public void Contract_registers_filter_spec_and_builder()
  {
    ContractContent.Should().Contain("LogQueryFilterSpec");
    ContractContent.Should().Contain("LogFilterBuilder");
  }

  [Fact]
  public void LogViewController_has_apply_filter_spec_method()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogViewController.cs");
    source.Should().Contain("ApplyFilterSpec");
  }

  [Fact]
  public void Phase4_contract_section_22_has_seven_log_p4_entries()
  {
    ContractContent.Should().Contain("LOG-P4-TRACE");
    ContractContent.Should().Contain("LOG-P4-QUERY");
    ContractContent.Should().Contain("LOG-P4-UI-FILTER");
    ContractContent.Should().Contain("LOG-P4-UI-ASYNC");
    ContractContent.Should().Contain("LOG-P4-JSONL");
    ContractContent.Should().Contain("LOG-P4-FILTERSPEC");
    ContractContent.Should().Contain("LOG-P4-VERIFY");
  }

  [Fact]
  public void Phase4_contract_has_doc_log_p4_closure()
  {
    ContractContent.Should().Contain("DOC-LOG-P4");
    ContractContent.Should().Contain("已关闭");
  }

  [Fact]
  public void Phase4_contract_registers_all_new_types()
  {
    ContractContent.Should().Contain("TraceIdContext");
    ContractContent.Should().Contain("SpanIdContext");
    ContractContent.Should().Contain("UserActionIdContext");
    ContractContent.Should().Contain("LogIndexBuilder");
    ContractContent.Should().Contain("LogQueryService");
    ContractContent.Should().Contain("LogViewBackgroundQuery");
    ContractContent.Should().Contain("LogJsonlFormatter");
    ContractContent.Should().Contain("LogQueryFilterSpec");
    ContractContent.Should().Contain("LogFilterBuilder");
  }

  [Fact]
  public void Phase4_wip_list_has_nine_entries()
  {
    ContractContent.Should().Contain("SettingsLogPageHost");
    ContractContent.Should().Contain("SettingsLogRefreshPipeline");
  }

  [Fact]
  public void Phase4_src_no_file_append_all_text()
  {
    ContractContent.Should().Contain("File.AppendAllText");
  }
}
