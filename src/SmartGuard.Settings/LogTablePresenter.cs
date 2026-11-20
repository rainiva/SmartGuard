using System.Collections.ObjectModel;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

/// <summary>
/// Owns only the rows currently displayed by the log table.
/// </summary>
public sealed class LogTablePresenter
{
  public const int MaxPageSize = 100;

  private readonly ObservableCollection<LogTableRow> _rows = new();

  public LogTablePresenter()
  {
    Rows = new ReadOnlyObservableCollection<LogTableRow>(_rows);
  }

  public ReadOnlyObservableCollection<LogTableRow> Rows { get; }

  public int PageSize { get; private set; }

  public void ApplyPage(IReadOnlyList<LogEvent> events, int pageSize)
  {
    ArgumentNullException.ThrowIfNull(events);
    if (pageSize <= 0 || pageSize > MaxPageSize)
      throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "页大小必须在 1 到 100 之间。");
    if (events.Count > MaxPageSize)
      throw new ArgumentOutOfRangeException(nameof(events), events.Count, "当前页不得超过 100 条日志。");

    _rows.Clear();
    foreach (var logEvent in events)
      _rows.Add(LogTableRow.FromEvent(logEvent));

    PageSize = pageSize;
  }
}
