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

    LogViewIdleReader.ApiReadOverrideForTests = () => 505;
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

  [Fact]
  public void TryReadSeconds_uses_local_api_when_user_becomes_active()
  {
    var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardIdleActive_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    var publishedAt = DateTime.Now.AddSeconds(-30);
    var status = new StatusPayload
    {
      idleSeconds = 500,
      timestamp = publishedAt.ToString("s"),
    };
    File.WriteAllText(
      Path.Combine(tempRoot, "SmartGuard.status.json"),
      JsonSerializer.Serialize(status));

    LogViewIdleReader.ApiReadOverrideForTests = () => 8;
    try
    {
      LogViewIdleReader.TryReadSeconds(tempRoot).Should().Be(8);
    }
    finally
    {
      LogViewIdleReader.ApiReadOverrideForTests = null;
      try { Directory.Delete(tempRoot, true); } catch { }
    }
  }

  [Fact]
  public void TryReadSeconds_keeps_extrapolated_when_user_still_idle()
  {
    var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardIdleStill_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    var publishedAt = new DateTime(2026, 6, 21, 10, 0, 0);
    var status = new StatusPayload
    {
      idleSeconds = 100,
      timestamp = publishedAt.ToString("s"),
    };
    File.WriteAllText(
      Path.Combine(tempRoot, "SmartGuard.status.json"),
      JsonSerializer.Serialize(status));

    LogViewIdleReader.ApiReadOverrideForTests = () => 120;
    try
    {
      LogViewIdleReader.TryReadSeconds(tempRoot, new DateTime(2026, 6, 21, 10, 0, 25))
        .Should().Be(125);
    }
    finally
    {
      LogViewIdleReader.ApiReadOverrideForTests = null;
      try { Directory.Delete(tempRoot, true); } catch { }
    }
  }

  [Fact]
  public void TryReadSeconds_uses_api_when_status_timestamp_too_old()
  {
    var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardIdleStale_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    var publishedAt = new DateTime(2026, 6, 21, 9, 0, 0);
    var status = new StatusPayload
    {
      idleSeconds = 500,
      timestamp = publishedAt.ToString("s"),
    };
    File.WriteAllText(
      Path.Combine(tempRoot, "SmartGuard.status.json"),
      JsonSerializer.Serialize(status));

    LogViewIdleReader.ApiReadOverrideForTests = () => 12;
    try
    {
      LogViewIdleReader.TryReadSeconds(tempRoot, new DateTime(2026, 6, 21, 10, 0, 0))
        .Should().Be(12);
    }
    finally
    {
      LogViewIdleReader.ApiReadOverrideForTests = null;
      try { Directory.Delete(tempRoot, true); } catch { }
    }
  }

  [Fact]
  public void TryRead_marks_status_stale_after_hint_threshold()
  {
    var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardIdleHint_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    var publishedAt = new DateTime(2026, 6, 21, 9, 58, 29);
    var status = new StatusPayload
    {
      idleSeconds = 40,
      timestamp = publishedAt.ToString("s"),
    };
    File.WriteAllText(
      Path.Combine(tempRoot, "SmartGuard.status.json"),
      JsonSerializer.Serialize(status));

    LogViewIdleReader.ApiReadOverrideForTests = () => 128;
    try
    {
      var result = LogViewIdleReader.TryRead(tempRoot, new DateTime(2026, 6, 21, 10, 0, 0));
      result.StatusMayBeStale.Should().BeTrue();
      result.Seconds.Should().Be(131);
    }
    finally
    {
      LogViewIdleReader.ApiReadOverrideForTests = null;
      try { Directory.Delete(tempRoot, true); } catch { }
    }
  }
}
