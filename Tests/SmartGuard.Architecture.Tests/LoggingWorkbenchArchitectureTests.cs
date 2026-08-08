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
    ContractContent.Should().Contain("DOC-LOG-WB | 已关闭",
      "P0 log workbench stage must be closed after P0-08");
    ContractContent.Should().Contain("## 24.");
    ContractContent.Should().Contain("LOG-WB-P1-AST");
    ContractContent.Should().Contain("LOG-WB-P1-DIAGNOSTIC");
    ContractContent.Should().Contain("DOC-LOG-WB-P1 | 已关闭",
      "P1 log workbench stage must be closed after P1-06");

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

  [Fact]
  public void LogJsonlEntry_migration_shell_is_removed()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "src/SmartGuard.Configuration/LogJsonlEntry.cs"))
      .Should().BeFalse("P0-08 removes the LogJsonlEntry adapter shell in favor of LogEvent");

    var references = SourceScanHelper.EnumerateCsFilesUnder("src")
      .Where(path => File.ReadAllText(path).Contains("LogJsonlEntry", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    references.Should().BeEmpty(
      "LogJsonlEntry must not remain in production code after P0-08; found: "
      + string.Join(", ", references));
  }

  [Fact]
  public void SavedLogFilterCoordinator_writes_only_via_GuardConfigRepository()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SavedLogFilterCoordinator.cs");
    source.Should().Contain("_repository.Save(");
    source.Should().NotContain("File.WriteAllText(");
    source.Should().NotContain("GuardConfigAtomicFileWriter");
  }

  [Fact]
  public void LogSessionProjector_is_the_single_session_boundary_owner()
  {
    var projectors = SourceScanHelper.EnumerateCsFilesUnder("src")
      .Where(path => File.ReadAllText(path).Contains("class LogSessionProjector", StringComparison.Ordinal))
      .Select(path => Path.GetRelativePath(SourceScanHelper.RepoRoot, path))
      .ToList();

    projectors.Should().ContainSingle(
      "session boundaries must stay on LogSessionProjector; found: "
      + string.Join(", ", projectors));
  }

  [Fact]
  public void Log_session_scope_has_single_controller_entrypoint_and_contract()
  {
    ContractContent.Should().Contain("LOG-WB-SESSION-SCOPE");
    ContractContent.Should().Contain("LogViewController.ScopeExpression");
    ContractContent.Should().Contain("LogSessionCoordinator");

    var filterState = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogView/LogViewFilterState.cs");
    filterState.Should().Contain("WithScope(");
    filterState.Should().Contain("LogQueryAndExpression");

    var controller = SourceScanHelper.ReadSource("src/SmartGuard.Settings/LogViewController.cs");
    controller.Should().Contain("ScopeExpression");
    controller.Should().Contain(".WithScope(ScopeExpression)");

    var sessionHost = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.Session.cs");
    sessionHost.Should().NotContain("LogQueryExpressionEvaluator.Matches",
      "host must not hand-filter session windows outside the controller scope pipeline");
    sessionHost.Should().NotContain("new LogQueryAndExpression",
      "host must not duplicate base AND session expression logic");
  }

  [Fact]
  public void Log_viewport_scroll_mutations_are_owned_by_LogViewportController_only()
  {
    var scrollApis = new[]
    {
      "ScrollToVerticalOffset",
      "ScrollToHorizontalOffset",
      "ScrollIntoView",
      "ScrollToEnd",
    };

    var violations = new List<string>();
    foreach (var file in EnumerateLogModuleSources())
    {
      if (file.EndsWith("LogViewportController.cs", StringComparison.OrdinalIgnoreCase))
        continue;

      var source = File.ReadAllText(file);
      var relativePath = Path.GetRelativePath(SourceScanHelper.RepoRoot, file);
      foreach (var api in scrollApis)
      {
        if (source.Contains(api, StringComparison.Ordinal))
          violations.Add($"{relativePath}: {api}");
      }
    }

    violations.Should().BeEmpty(
      "only LogViewportController may mutate log ScrollViewer offsets; violations: "
      + string.Join(", ", violations));
  }

  private static IEnumerable<string> EnumerateLogModuleSources()
  {
    return SourceScanHelper.EnumerateCsFilesUnder("src/SmartGuard.Settings")
      .Where(IsLogModuleSourceFile);
  }

  private static bool IsLogModuleSourceFile(string path)
  {
    var fileName = Path.GetFileName(path);
    return fileName.StartsWith("SettingsLog", StringComparison.Ordinal)
      || fileName.StartsWith("LogView", StringComparison.Ordinal)
      || fileName.StartsWith("LogLine", StringComparison.Ordinal)
      || fileName.StartsWith("LogSearch", StringComparison.Ordinal)
      || fileName.StartsWith("LogTag", StringComparison.Ordinal)
      || fileName.StartsWith("LogViewport", StringComparison.Ordinal);
  }
}
