namespace SmartGuard.Configuration.Tests;

public class PauseGuardMessagesTests
{
  [Fact]
  public void GetLogMessage_returns_pause_text_when_enabling_pause()
  {
    PauseGuardMessages.GetLogMessage(false, true)
      .Should().Be("守护已暂停（仅监控，不切换计划）");
  }

  [Fact]
  public void GetLogMessage_returns_null_when_unchanged()
  {
    PauseGuardMessages.GetLogMessage(true, true).Should().BeNull();
  }
}
