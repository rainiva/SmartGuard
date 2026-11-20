namespace SmartGuard.Configuration;

public sealed class SavedLogFilter
{
  public string Id { get; set; } = Guid.NewGuid().ToString("N");

  public string Name { get; set; } = string.Empty;

  public LogQueryExpressionDto Query { get; set; } = LogQueryExpressionDto.MatchAll();
}
