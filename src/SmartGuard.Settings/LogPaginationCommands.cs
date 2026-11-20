using System.Windows.Input;
using System.Globalization;

namespace SmartGuard.Settings;

public static class LogPaginationCommands
{
  public static readonly RoutedUICommand FirstPage = new("第一页", nameof(FirstPage), typeof(LogPaginationCommands));
  public static readonly RoutedUICommand PreviousPage = new("上一页", nameof(PreviousPage), typeof(LogPaginationCommands));
  public static readonly RoutedUICommand GoToPage = new("转到页", nameof(GoToPage), typeof(LogPaginationCommands));
  public static readonly RoutedUICommand NextPage = new("下一页", nameof(NextPage), typeof(LogPaginationCommands));
  public static readonly RoutedUICommand LastPage = new("最后一页", nameof(LastPage), typeof(LogPaginationCommands));

  internal static bool CanExecute(RoutedUICommand command, LogPaginationState state, object? parameter = null)
  {
    ArgumentNullException.ThrowIfNull(command);
    ArgumentNullException.ThrowIfNull(state);

    if (ReferenceEquals(command, FirstPage))
      return state.CanGoFirst;
    if (ReferenceEquals(command, PreviousPage))
      return state.CanGoPrevious;
    if (ReferenceEquals(command, NextPage))
      return state.CanGoNext;
    if (ReferenceEquals(command, LastPage))
      return state.CanGoLast;
    if (ReferenceEquals(command, GoToPage))
      return TryGetPageNumber(parameter, out var pageNumber)
        && state.CanNavigateToPage(pageNumber);

    return false;
  }

  internal static bool TryGetPageNumber(object? parameter, out int pageNumber)
  {
    switch (parameter)
    {
      case int value:
        pageNumber = value;
        return true;
      case LogPageNumberToken token:
        pageNumber = token.PageNumber;
        return true;
      case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
        pageNumber = parsed;
        return true;
      default:
        pageNumber = 0;
        return false;
    }
  }
}
