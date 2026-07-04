using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class GitHubTokenProtectorTests
{
  [Fact]
  public void Protect_and_Unprotect_roundtrip()
  {
    const string token = "ghp_testRoundtripToken1234567890";

    var protectedValue = GitHubTokenProtector.Protect(token);
    protectedValue.Should().NotBeNullOrWhiteSpace();
    protectedValue.Should().NotBe(token);

    GitHubTokenProtector.TryUnprotect(protectedValue).Should().Be(token);
  }

  [Fact]
  public void Protect_empty_string_returns_null()
  {
    GitHubTokenProtector.Protect("").Should().BeNull();
    GitHubTokenProtector.Protect("   ").Should().BeNull();
  }

  [Fact]
  public void TryUnprotect_invalid_cipher_returns_null()
  {
    GitHubTokenProtector.TryUnprotect("not-valid-dpapi").Should().BeNull();
    GitHubTokenProtector.TryUnprotect("").Should().BeNull();
  }
}
