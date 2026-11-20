namespace SmartGuard.Configuration;

public sealed class LogViewPreferences
{
  public List<SavedLogFilter> SavedFilters { get; set; } = [];

  public string? DefaultFilterId { get; set; }
}
