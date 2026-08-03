using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractLoggingPhase1Tests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Contract_registers_exception_formatter()
  {
    ContractContent.Should().Contain("ExceptionFormatter");
  }

  [Fact]
  public void No_direct_File_AppendAllText_outside_unified_log_writer()
  {
    var offenders = SourceScanHelper.EnumerateCsFilesUnder("src")
      .Where(f => !f.EndsWith("SmartGuardLogWriter.cs", StringComparison.OrdinalIgnoreCase))
      .Where(f => File.ReadAllText(f).Contains("File.AppendAllText"))
      .Select(Path.GetFileName)
      .ToList();

    offenders.Should().BeEmpty(
      "日志追加必须经 SmartGuardLogWriter.TryAppendLine，禁止散落 File.AppendAllText 直写");
  }

  [Fact]
  public void All_four_process_entrypoints_register_global_exception_handlers()
  {
    var programs = new[]
    {
      "src/SmartGuard.Engine/Program.cs",
      "src/SmartGuard.Tray/Program.cs",
      "src/SmartGuard.Settings/Program.cs",
      "src/SmartGuard.Cli/Program.cs",
    };

    foreach (var program in programs)
    {
      SourceScanHelper.ReadSource(program)
        .Should().Contain("GlobalExceptionHandlers.Register",
          $"{program} 必须在启动时注册全局未处理异常处理器");
    }
  }

  [Fact]
  public void GuardianLoop_WriteLog_uses_three_layer_fallback_chain()
  {
    var source = SourceScanHelper.ReadGuardianLoopSource();
    source.Should().Contain("LogFallbackChain.WriteEvent",
      "WriteLog 必须走三层降级链而非单层 fallback");
    source.Should().Contain("LogStructuredSink.TryWriteBatch",
      "主层写入必须使用结构化 sink 批量落盘");
  }
}
