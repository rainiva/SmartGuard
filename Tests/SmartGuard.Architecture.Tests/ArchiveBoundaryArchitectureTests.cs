using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchiveBoundaryArchitectureTests
{
  [Fact]
  public void Contracts_must_define_archive_paths_as_the_only_legacy_truth_boundary()
  {
    var contract = SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");
    var acceptance = SourceScanHelper.ReadSource("docs/MULTI-SOURCE-GOVERNANCE-ACCEPTANCE.md");
    var overview = SourceScanHelper.ReadSource("docs/architecture/single-entry-single-source.md");

    contract.Should().Contain("docs/archive/");
    contract.Should().Contain("scripts/archive/");
    acceptance.Should().Contain("docs/archive/");
    acceptance.Should().Contain("scripts/archive/");
    overview.Should().Contain("docs/archive/");
  }

  [Fact]
  public void Acceptance_docs_must_record_that_active_scripts_can_no_longer_regenerate_retired_lib_assets()
  {
    var acceptance = SourceScanHelper.ReadSource("docs/MULTI-SOURCE-GOVERNANCE-ACCEPTANCE.md");
    acceptance.Should().Contain("scripts/archive/");
    acceptance.Should().Contain("generate-smartguard-icons.py");
    acceptance.Should().Contain(@"lib\SmartGuard.ico");
  }
}
