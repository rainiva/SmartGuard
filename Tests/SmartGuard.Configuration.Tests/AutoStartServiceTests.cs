namespace SmartGuard.Configuration.Tests;

public class AutoStartServiceTests
{
  [Theory]
  [InlineData(true, true, false)]
  [InlineData(false, false, false)]
  [InlineData(true, false, true)]
  [InlineData(true, null, true)]
  public void NeedsUpdate_matches_ps_contract(bool enabled, bool? previous, bool expected)
  {
    AutoStartService.NeedsUpdate(enabled, previous).Should().Be(expected);
  }

  [Fact]
  public void ScheduledTaskNames_delegate_to_registrar()
  {
    AutoStartService.ScheduledTaskNames.Should().BeEquivalentTo(ScheduledTaskRegistrar.TaskNames);
  }
}
