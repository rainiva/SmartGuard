using System.Text.Json;
using SmartGuard.Contracts;

namespace SmartGuard.Settings.Tests;

public class LogViewIdleReaderTests
{
  [Fact]
  public void TryReadSeconds_uses_override_when_configured()
  {
    LogViewIdleReader.ReadOverrideForTests = () => 123;
    try
    {
      LogViewIdleReader.TryReadSeconds("C:\\unused").Should().Be(123);
    }
    finally
    {
      LogViewIdleReader.ReadOverrideForTests = null;
    }
  }

  [Fact]
  public void TryReadSeconds_returns_null_when_override_throws()
  {
    LogViewIdleReader.ReadOverrideForTests = () => throw new InvalidOperationException("boom");
    try
    {
      LogViewIdleReader.TryReadSeconds("C:\\unused").Should().BeNull();
    }
    finally
    {
      LogViewIdleReader.ReadOverrideForTests = null;
    }
  }

  [Fact]
  public void TryReadSeconds_prefers_status_file_over_local_input_api()
  {
    var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardIdleRead_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    var publishedAt = DateTime.Now.AddSeconds(-20);
    var status = new StatusPayload
    {
      idleSeconds = 480,
      timestamp = publishedAt.ToString("s"),
    };
    File.WriteAllText(
      Path.Combine(tempRoot, "SmartGuard.status.json"),
      JsonSerializer.Serialize(status));

    LogViewIdleReader.ApiReadOverrideForTests = () => 0;
    try
    {
      var idle = LogViewIdleReader.TryReadSeconds(tempRoot);
      idle.Should().BeGreaterThanOrEqualTo(500);
      idle.Should().BeLessThan(520);
    }
    finally
    {
      LogViewIdleReader.ApiReadOverrideForTests = null;
      try { Directory.Delete(tempRoot, true); } catch { }
    }
  }
}
