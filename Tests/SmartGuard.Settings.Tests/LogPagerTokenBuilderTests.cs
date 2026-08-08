using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

public class LogPagerTokenBuilderTests
{
  [Theory]
  [InlineData(1, 63, "1 2 3 4 5 … 63")]
  [InlineData(4, 63, "1 2 3 4 5 … 63")]
  [InlineData(5, 63, "1 … 4 5 6 … 63")]
  [InlineData(60, 63, "1 … 59 60 61 62 63")]
  [InlineData(63, 63, "1 … 59 60 61 62 63")]
  public void Build_matches_reference_window(int currentPage, int totalPages, string expected)
  {
    var tokens = LogPagerTokenBuilder.Build(currentPage, totalPages);

    Render(tokens).Should().Be(expected);
    tokens
      .OfType<LogPageNumberToken>()
      .Select(token => token.PageNumber)
      .Should()
      .OnlyHaveUniqueItems();
  }

  [Theory]
  [InlineData(0, 0)]
  [InlineData(-5, 0)]
  [InlineData(3, -10)]
  public void Build_non_positive_total_pages_returns_page_one_only(int currentPage, int totalPages)
  {
    var tokens = LogPagerTokenBuilder.Build(currentPage, totalPages);

    tokens.Should().ContainSingle()
      .Which.Should().Be(new LogPageNumberToken(1, true));
  }

  private static string Render(IReadOnlyList<LogPagerToken> tokens)
    => string.Join(
      " ",
      tokens.Select(
        token => token switch
        {
          LogPageNumberToken number => number.PageNumber.ToString(),
          LogPageEllipsisToken => "…",
          _ => throw new InvalidOperationException("Unexpected pager token type."),
        }));
}
