using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class SettingsPauseDisplayArchitectureTests
{
  [Fact]
  public void SettingsPolicyCoordinator_must_init_pause_from_status_not_config()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsPolicyCoordinator.cs");
    var applyInitial = ExtractMethodBody(source, "ApplyInitialValues");
    applyInitial.Should().NotContain(
      "_tglPaused.IsChecked = config.Paused",
      "pause toggle display must read status.json, not config.Paused");
    applyInitial.Should().Contain("StatusJsonReader");
  }

  private static string ExtractMethodBody(string source, string methodName)
  {
    var start = source.IndexOf($"void {methodName}(", StringComparison.Ordinal);
    start.Should().BeGreaterThan(-1, $"expected method {methodName}");
    var braceStart = source.IndexOf('{', start);
    var depth = 0;
    for (var i = braceStart; i < source.Length; i++)
    {
      if (source[i] == '{') depth++;
      else if (source[i] == '}')
      {
        depth--;
        if (depth == 0)
          return source[braceStart..(i + 1)];
      }
    }

    throw new InvalidOperationException($"Could not extract body for {methodName}");
  }
}
