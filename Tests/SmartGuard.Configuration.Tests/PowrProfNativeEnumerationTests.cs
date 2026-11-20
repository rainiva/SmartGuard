using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class PowrProfNativeEnumerationTests
{
  private const uint NoMoreItems = 259;

  private static Func<uint, (uint Rc, Guid Scheme)> ListOf(int count, uint failAt = uint.MaxValue, uint failCode = 0)
    => index => index == failAt ? (failCode, Guid.Empty)
      : index < count ? (0u, new Guid((int)index + 1, 0, 0, new byte[8]))
      : (NoMoreItems, Guid.Empty);

  [Fact]
  public void A_list_that_ends_at_the_cap_is_complete()
  {
    PowrProfNative.EnumerateBounded(ListOf(3), 3, out var schemes).Should().Be(0u);
    schemes.Should().HaveCount(3);
  }

  [Fact]
  public void A_list_longer_than_the_cap_is_reported_as_cut_never_as_complete()
  {
    var calls = 0;
    var source = ListOf(5);
    PowrProfNative.EnumerateBounded(index => { calls++; return source(index); }, 3, out var schemes)
      .Should().Be(234u, "a cut list must not let callers conclude that unlisted schemes are absent");
    schemes.Should().BeEmpty();
    calls.Should().Be(4, "one read past the cap is enough to know the list was cut");
  }

  [Fact]
  public void An_api_error_is_returned_as_is()
  {
    PowrProfNative.EnumerateBounded(ListOf(5, failAt: 1, failCode: 5), 3, out var schemes).Should().Be(5u);
    schemes.Should().BeEmpty();
  }
}
