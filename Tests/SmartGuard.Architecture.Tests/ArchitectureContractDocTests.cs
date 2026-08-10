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
  public void Rule_entrypoint_routes_architecture_tdd_and_critical_module_governance()
  {
    var agentsPath = Path.Combine(RepoRoot, "AGENTS.md");
    var projectRulesPath = Path.Combine(RepoRoot, "docs", "PROJECT-RULES.md");
    var agents = File.ReadAllText(agentsPath);
    var projectRules = File.ReadAllText(projectRulesPath);

    agents.Should().Contain("行为改动必须 TDD");
    agents.Should().Contain("SmartGuard 架构硬门禁");
    agents.Should().Contain("关键模块治理");
    projectRules.Should().Contain("### 2.5 关键模块治理");
    projectRules.Should().Contain("模块不变量");
  }
}
