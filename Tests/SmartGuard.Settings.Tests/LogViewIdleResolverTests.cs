using System.Text.Json;
using SmartGuard.Contracts;

namespace SmartGuard.Settings.Tests;

public class LogViewIdleResolverTests
{
  [Fact]
  public void ResolveFromStatus_adds_elapsed_seconds_since_timestamp()
  {
    var status = new StatusPayload
    {
      idleSeconds = 100,
      timestamp = "2026-06-21T10:00:00",
    };

    LogViewIdleResolver.ResolveFromStatus(status, new DateTime(2026, 6, 21, 10, 0, 25))
      .Should().Be(125);
  }

  [Fact]
  public void ResolveFromStatus_returns_published_idle_when_timestamp_missing()
  {
    var status = new StatusPayload
    {
      idleSeconds = 480,
      timestamp = string.Empty,
    };

    LogViewIdleResolver.ResolveFromStatus(status, DateTime.Now)
      .Should().Be(480);
  }
}
