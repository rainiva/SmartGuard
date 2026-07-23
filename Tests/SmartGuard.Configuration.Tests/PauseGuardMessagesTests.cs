namespace SmartGuard.Configuration.Tests;

public class PauseGuardMessagesTests
{
  [Fact]
  public void GetLogMessage_returns_pause_text_when_enabling_pause()
  {
    PauseGuardMessages.GetLogMessage(false, true)
      .Should().Be("暂停守护选项已开启");
  }

  [Fact]
  public void GetLogMessage_returns_resume_text_when_disabling_pause()
  {
    PauseGuardMessages.GetLogMessage(true, false)
      .Should().Be("暂停守护选项已关闭");
  }

  [Fact]
  public void GetLogMessage_returns_null_when_unchanged()
  {
    PauseGuardMessages.GetLogMessage(true, true).Should().BeNull();
  }
}
