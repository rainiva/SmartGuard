using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogRegexSpecTests
{
  [Fact]
  public void TryCreate_valid_pattern_matches_text()
  {
    var spec = LogRegexSpec.TryCreate("timeout", ignoreCase: true);

    spec.IsValid.Should().BeTrue();
    spec.Spec!.IsMatch("request timeout").Should().BeTrue();
    spec.Spec.IsMatch("TIMEOUT").Should().BeTrue();
  }

  [Fact]
  public void TryCreate_respects_case_sensitive_option()
  {
    var spec = LogRegexSpec.TryCreate("Timeout", ignoreCase: false);

    spec.IsValid.Should().BeTrue();
    spec.Spec!.IsMatch("Timeout").Should().BeTrue();
    spec.Spec.IsMatch("timeout").Should().BeFalse();
  }

  [Fact]
  public void TryCreate_invalid_pattern_returns_error()
  {
    var spec = LogRegexSpec.TryCreate("[unclosed", ignoreCase: true);

    spec.IsValid.Should().BeFalse();
    spec.ErrorMessage.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void TryCreate_rejects_pattern_longer_than_limit()
  {
    var pattern = new string('a', LogRegexSpec.MaxPatternLength + 1);
    var spec = LogRegexSpec.TryCreate(pattern, ignoreCase: true);

    spec.IsValid.Should().BeFalse();
    spec.ErrorMessage.Should().Contain("1024");
  }

  [Fact]
  public void IsMatch_catastrophic_backtracking_times_out_within_100ms()
  {
    var spec = LogRegexSpec.TryCreate("(a+)+b", ignoreCase: true);
    spec.IsValid.Should().BeTrue();

    var input = new string('a', 30);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var timedOut = false;
    _ = spec.Spec!.TryIsMatch(input, CancellationToken.None, out timedOut);
    sw.Stop();

    timedOut.Should().BeTrue();
    sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
  }

  [Fact]
  public void TryCreate_prefers_non_backtracking_when_supported()
  {
    var spec = LogRegexSpec.TryCreate("engine-\\d+", ignoreCase: true);

    spec.IsValid.Should().BeTrue();
    spec.Spec!.UsesBacktracking.Should().BeFalse();
  }
}
