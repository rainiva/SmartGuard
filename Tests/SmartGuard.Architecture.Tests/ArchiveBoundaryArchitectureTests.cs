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
  public void Acceptance_docs_must_record_that_live_icon_truth_is_official_source_frames_plus_canonical_branding_asset()
  {
    var acceptance = SourceScanHelper.ReadSource("docs/MULTI-SOURCE-GOVERNANCE-ACCEPTANCE.md");
    acceptance.Should().Contain("scripts/archive/");
    acceptance.Should().Contain("assets/branding/official");
    acceptance.Should().Contain("assets/branding/SmartGuard.ico");
    acceptance.Should().Contain("assets/icon");
  }
}
