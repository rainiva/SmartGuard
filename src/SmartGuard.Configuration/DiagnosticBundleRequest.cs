namespace SmartGuard.Configuration;

public sealed record DiagnosticBundleRequest
{
  public required string Root { get; init; }

  public required string DestinationZipPath { get; init; }

  public required ILogEventSource EventSource { get; init; }

  public string? AppVersion { get; init; }

  public DiagnosticBundleScope Scope { get; init; } = DiagnosticBundleScope.FullAvailableHistory;

  public LogQueryExpression? FilterExpression { get; init; }

  public LogOperationCorrelationKey? OperationKey { get; init; }

  public DateTimeOffset? TimeRangeStart { get; init; }

  public DateTimeOffset? TimeRangeEnd { get; init; }

  public IReadOnlyList<LogEvent>? RecentWindowEvents { get; init; }

  public LogSourceCatalog? CatalogSnapshot { get; init; }

  public bool OverwriteExisting { get; init; }

  public bool IncludeStartupLog { get; init; } = true;

  public bool IncludeEmergencyLog { get; init; }

  public bool IncludeCrashLog { get; init; }
}
