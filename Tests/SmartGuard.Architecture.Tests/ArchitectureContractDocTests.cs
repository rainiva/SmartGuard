namespace SmartGuard.Architecture.Tests;

using FluentAssertions;

public class ArchitectureContractDocTests
{
  private static string RepoRoot =>
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

  [Fact]
  public void Architecture_contract_document_exists_with_required_sections()
  {
    var path = Path.Combine(RepoRoot, "docs", "ARCHITECTURE-CONTRACT.md");
    File.Exists(path).Should().BeTrue();

    var content = File.ReadAllText(path);
    content.Should().Contain("真源注册表");
    content.Should().Contain("入口注册表");
    content.Should().Contain("禁止清单");
    content.Should().Contain("GuardConfigRepository");
    content.Should().Contain("InstallRootResolver");
  }

  [Fact]
  public void Agents_md_contains_architecture_and_tdd_governance_sections()
  {
    var path = Path.Combine(RepoRoot, "AGENTS.md");
    var content = File.ReadAllText(path);
    content.Should().Contain("### 8. 架构真源");
    content.Should().Contain("### 11. 多真源治理专项 TDD");
  }
}
