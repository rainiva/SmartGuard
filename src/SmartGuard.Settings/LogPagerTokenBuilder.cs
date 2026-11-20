namespace SmartGuard.Settings;

internal static class LogPagerTokenBuilder
{
  internal static IReadOnlyList<LogPagerToken> Build(int currentPage, int totalPages)
  {
    if (totalPages <= 0)
      return [new LogPageNumberToken(1, true)];

    var normalizedCurrentPage = Math.Clamp(currentPage, 1, totalPages);
    if (totalPages <= 7)
      return BuildRange(1, totalPages, normalizedCurrentPage);

    if (normalizedCurrentPage <= 4)
    {
      return
      [
        new LogPageNumberToken(1, normalizedCurrentPage == 1),
        new LogPageNumberToken(2, normalizedCurrentPage == 2),
        new LogPageNumberToken(3, normalizedCurrentPage == 3),
        new LogPageNumberToken(4, normalizedCurrentPage == 4),
        new LogPageNumberToken(5, normalizedCurrentPage == 5),
        new LogPageEllipsisToken(),
        new LogPageNumberToken(totalPages, normalizedCurrentPage == totalPages),
      ];
    }

    if (normalizedCurrentPage >= totalPages - 3)
    {
      return
      [
        new LogPageNumberToken(1, normalizedCurrentPage == 1),
        new LogPageEllipsisToken(),
        new LogPageNumberToken(totalPages - 4, normalizedCurrentPage == totalPages - 4),
        new LogPageNumberToken(totalPages - 3, normalizedCurrentPage == totalPages - 3),
        new LogPageNumberToken(totalPages - 2, normalizedCurrentPage == totalPages - 2),
        new LogPageNumberToken(totalPages - 1, normalizedCurrentPage == totalPages - 1),
        new LogPageNumberToken(totalPages, normalizedCurrentPage == totalPages),
      ];
    }

    return
    [
      new LogPageNumberToken(1, normalizedCurrentPage == 1),
      new LogPageEllipsisToken(),
      new LogPageNumberToken(normalizedCurrentPage - 1, false),
      new LogPageNumberToken(normalizedCurrentPage, true),
      new LogPageNumberToken(normalizedCurrentPage + 1, false),
      new LogPageEllipsisToken(),
      new LogPageNumberToken(totalPages, normalizedCurrentPage == totalPages),
    ];
  }

  private static IReadOnlyList<LogPagerToken> BuildRange(int startPage, int endPage, int currentPage)
  {
    var tokens = new List<LogPagerToken>(endPage - startPage + 1);
    for (var page = startPage; page <= endPage; page++)
      tokens.Add(new LogPageNumberToken(page, page == currentPage));
    return tokens;
  }
}
