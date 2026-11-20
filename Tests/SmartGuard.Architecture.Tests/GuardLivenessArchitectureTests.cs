using System.Text;
using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class GuardLivenessArchitectureTests
{
  [Fact]
  public void Only_the_iteration_runner_assigns_guard_health()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;
      var name = Path.GetFileName(file);
      // The two clones copy the field; the runner is the only producer.
      if (name is "GuardianIterationRunner.cs" or "StatusPublisher.cs" or "TrayDisplayState.cs") continue;
      File.ReadAllText(file, Encoding.UTF8).Should().NotContain("guardHealth =", $"{file} is not the guard health owner");
    }
  }

  [Fact]
  public void Liveness_rule_lives_in_one_tracker()
  {
    var runner = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianIterationRunner.Liveness.cs");
    runner.Should().Contain("_liveness.Observe(").And.NotContain("Threshold");
    SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardLivenessTracker.cs")
      .Should().Contain("TimeSpan.FromMinutes(10)");
  }

  [Fact]
  public void Contract_registers_the_liveness_invariant()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("GUARD-INV-LIVENESS").And.Contain("guardHealth").And.Contain("GuardLivenessTracker");
  }
}
