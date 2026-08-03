using System.Windows.Input;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class LogMatchNavigationWpfTests
{
  [Fact]
  public void F3_and_ShiftF3_delegate_to_match_navigator()
  {
    LogMatchNavigationDirection? direction = null;
    LogMatchNavigationCommands.TryHandleKey(Key.F3, ModifierKeys.None, d => direction = d)
      .Should().BeTrue();
    direction.Should().Be(LogMatchNavigationDirection.Forward);

    LogMatchNavigationCommands.TryHandleKey(Key.F3, ModifierKeys.Shift, d => direction = d)
      .Should().BeTrue();
    direction.Should().Be(LogMatchNavigationDirection.Backward);
  }

  [Fact]
  public void Enter_and_ShiftEnter_delegate_to_match_navigator()
  {
    LogMatchNavigationDirection? direction = null;
    LogMatchNavigationCommands.TryHandleKey(Key.Enter, ModifierKeys.None, d => direction = d)
      .Should().BeTrue();
    direction.Should().Be(LogMatchNavigationDirection.Forward);

    LogMatchNavigationCommands.TryHandleKey(Key.Enter, ModifierKeys.Shift, d => direction = d)
      .Should().BeTrue();
    direction.Should().Be(LogMatchNavigationDirection.Backward);
  }
}
