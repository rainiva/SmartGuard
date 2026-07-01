using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class GuardConfigRepositoryArchitectureTests
{
  [Fact]
  public void GuardConfigRepository_must_not_expose_UpdatePaused()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardConfigRepository.cs");
    source.Should().NotContain(
      "public void UpdatePaused",
      "pause writes must go through ConfigMutationService only");
  }
}
