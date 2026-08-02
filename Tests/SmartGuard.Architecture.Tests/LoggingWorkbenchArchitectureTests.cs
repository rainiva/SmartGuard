using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class LoggingWorkbenchArchitectureTests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  private static string AdrRelativePath =>
    "docs/decisions/0001-log-workbench-architecture.md";

  [Fact]
  public void Log_history_reader_remains_LogTailReader()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, AdrRelativePath))
      .Should().BeTrue("log workbench ADR must exist before expanding history reader ownership");

    ContractContent.Should().Contain("0001-log-workbench-architecture.md");
    ContractContent.Should().Contain("LOG-WB-HISTORY");
    ContractContent.Should().Contain("LogTailReader");

    var historyReaders = SourceScanHelper.EnumerateCsFilesUnder("src")
      .Where(path => !path.EndsWith("LogTailReaderTestMetrics.cs", StringComparison.OrdinalIgnoreCase))
      .Where(path => File.ReadAllText(path).Contains("class LogTailReader", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    historyReaders.Should().ContainSingle(
      "configured log history must stay on LogTailReader; found: "
      + string.Join(", ", historyReaders));
  }

  [Fact]
  public void LogIndexBuilder_does_not_open_log_files_directly()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/LogIndexBuilder.cs");
    source.Should().NotContain("new FileStream");
    source.Should().NotContain("File.OpenRead");
    source.Should().NotContain("File.Exists");
    source.Should().NotContain("StreamReader");
    source.Should().NotContain("ReadAllText");
    source.Should().NotContain("ReadAllLines");
  }

  [Fact]
  public void Log_queries_remain_LogQueryService()
  {
    ContractContent.Should().Contain("LOG-WB-QUERY");
    ContractContent.Should().Contain("LogQueryService");
    ContractContent.Should().Contain("LogIndexBuilder");

    var queryServices = SourceScanHelper.EnumerateCsFilesUnder("src")
      .Where(path => File.ReadAllText(path).Contains("class LogQueryService", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    queryServices.Should().ContainSingle(
      "log queries must stay on LogQueryService; found: "
      + string.Join(", ", queryServices));
  }

  [Fact]
  public void Log_config_and_diagnostic_export_keep_existing_owners()
  {
    ContractContent.Should().Contain("## 23.");
    ContractContent.Should().Contain("LOG-WB-CONFIG");
    ContractContent.Should().Contain("LOG-WB-EXPORT");
    ContractContent.Should().Contain("GuardConfigRepository");
    ContractContent.Should().Contain("DiagnosticBundleExporter");
    ContractContent.Should().Contain("LoggingWorkbenchArchitectureTests");
    ContractContent.Should().NotContain("DOC-LOG-WB | 已关闭",
      "P0 log workbench stage must remain open until later slices close it");

    var configRepositories = SourceScanHelper.EnumerateCsFilesUnder("src")
      .Where(path => File.ReadAllText(path).Contains("class GuardConfigRepository", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    configRepositories.Should().ContainSingle(
      "log view preferences and filters must stay on GuardConfigRepository; found: "
      + string.Join(", ", configRepositories));

    var exporters = SourceScanHelper.EnumerateCsFilesUnder("src")
      .Where(path => File.ReadAllText(path).Contains("class DiagnosticBundleExporter", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    exporters.Should().ContainSingle(
      "diagnostic export must stay on DiagnosticBundleExporter; found: "
      + string.Join(", ", exporters));
  }

  [Fact]
  public void LogEventWindow_is_the_single_bounded_display_window()
  {
    var boundedWindows = SourceScanHelper.EnumerateCsFilesUnder("src")
      .Where(path => File.ReadAllText(path).Contains("class LogEventWindow", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    boundedWindows.Should().ContainSingle(
      "bounded log display window must stay on LogEventWindow; found: "
      + string.Join(", ", boundedWindows));
  }

  [Fact]
  public void LogViewListPresenter_does_not_clear_observable_collection()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogViewListPresenter.cs");
    source.Should().NotContain("_items.Clear(",
      "incremental log list updates must not reset the observable collection");
  }
}
