using System.Text.Json;
using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

public class LoggingHealthPublishingTests
{
  [Fact]
  public void Health_code_changes_publish_but_stable_samples_wait_for_existing_freshness()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-health-status-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var path = Path.Combine(root, "status.json");
      var time = new HealthClock();
      var publisher = new StatusPublisher(path, time);
      publisher.Publish(Payload("healthy", time.GetUtcNow()));
      var first = File.ReadAllText(path);
      using (var json = JsonDocument.Parse(first))
      {
        var health = json.RootElement.GetProperty("logHealth");
        health.GetProperty("code").GetString().Should().Be("healthy");
        health.GetProperty("observedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        health.GetProperty("primaryWrittenLines").GetInt64().Should().Be(1);
      }
      time.Advance(5);
      publisher.Publish(Payload("healthy", time.GetUtcNow(), primary: 20));
      File.ReadAllText(path).Should().Be(first);
      publisher.Publish(Payload("failed", time.GetUtcNow(), lost: 1));
      File.ReadAllText(path).Should().NotBe(first);
      var failure = File.ReadAllText(path);
      time.Advance(5);
      publisher.Publish(Payload("failed", time.GetUtcNow(), lost: 2));
      File.ReadAllText(path).Should().Be(failure);
      time.Advance(55);
      publisher.Publish(Payload("failed", time.GetUtcNow(), lost: 3));
      using var refreshed = JsonDocument.Parse(File.ReadAllText(path));
      refreshed.RootElement.GetProperty("logHealth").GetProperty("lostLines").GetInt64().Should().Be(3);
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public void Health_only_publication_does_not_advance_guard_observation_time()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-health-only-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var path = Path.Combine(root, "status.json");
      var publisher = new StatusPublisher(path);
      var observed = DateTimeOffset.UtcNow.AddMinutes(-4);
      publisher.Publish(Payload("healthy", observed));
      var update = typeof(StatusPublisher).GetMethod("PublishLogHealth");
      update.Should().NotBeNull("light cycles must publish health through the existing status owner");
      var fresh = Payload("failed", DateTimeOffset.UtcNow, lost: 1);
      var health = typeof(StatusPayload).GetProperty("logHealth")!.GetValue(fresh);
      update!.Invoke(publisher, [health]);
      var result = JsonSerializer.Deserialize<StatusPayload>(File.ReadAllText(path))!;
      result.timestamp.Should().Be(observed.ToString("O"));
      result.guardReason.Should().Be(GuardStatusCodes.Reasons.ForegroundMatched);
      GuardStatusFreshness.IsFresh(result.timestamp, DateTimeOffset.UtcNow).Should().BeFalse();
    }
    finally { Directory.Delete(root, true); }
  }

  private static StatusPayload Payload(string code, DateTimeOffset observed, long primary = 1, long lost = 0)
    => JsonSerializer.Deserialize<StatusPayload>(JsonSerializer.Serialize(new
    {
      timestamp = observed.ToString("O"), currentPlan = "平衡", idleSeconds = 1,
      guardSource = GuardStatusCodes.Sources.Automatic,
      guardReason = GuardStatusCodes.Reasons.ForegroundMatched,
      logHealth = new { code, observedAtUtc = observed, primaryWrittenLines = primary,
        fallbackWrittenLines = 0, lostLines = lost, droppedNormal = 0, droppedHigh = 0, droppedByBackpressure = 0 }
    }))!;

  private sealed class HealthClock : TimeProvider
  {
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero).AddTicks(_ticks);
    public void Advance(int seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
  }
}
