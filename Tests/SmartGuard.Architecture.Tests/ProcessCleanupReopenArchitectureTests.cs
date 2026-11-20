using System.Text;
using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ProcessCleanupReopenArchitectureTests
{
  [Fact]
  public void Wmi_probe_holds_an_unconfirmed_child_instead_of_a_bare_latch()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Engine/Infrastructure/WmiProbeProcess.cs");
    source.Should().NotContain("_closed").And.Contain("UnconfirmedChild? _pending");
  }

  [Fact]
  public void Only_the_wmi_probe_retains_unconfirmed_children()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;
      if (file.EndsWith("WmiProbeProcess.cs", StringComparison.Ordinal)) continue;
      File.ReadAllText(file, Encoding.UTF8).Should().NotContain("retainUnconfirmed: true", $"{file} is not a registered holder");
    }
  }

  [Fact]
  public void Contract_registers_the_cleanup_reopen_invariant()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    contract.Should().Contain("PWR-INV-CLEANUP-REOPEN").And.Contain("UnconfirmedChild");
    contract.Should().NotContain("清理未确认后关闭后续启动");
  }
}
