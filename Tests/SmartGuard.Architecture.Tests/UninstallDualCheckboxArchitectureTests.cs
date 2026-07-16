using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class UninstallDualCheckboxArchitectureTests
{
  [Fact]
  public void Iss_offers_separate_config_and_log_delete_checkboxes_on_custom_uninstall_page()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");

    iss.Should().Contain("DeleteUserConfig");
    iss.Should().Contain("DeleteUserLogs");
    iss.Should().Contain("ShouldDeleteUserConfig");
    iss.Should().Contain("ShouldDeleteUserLogs");
    iss.Should().Contain("删除用户数据（配置与状态）");
    iss.Should().Contain("删除日志文件");
    iss.Should().Contain("卸载 SmartGuard");

    iss.Should().NotContain("是否同时删除配置与日志文件");
    iss.Should().NotContain("说明：不勾选时保留上述文件");
  }

  [Fact]
  public void Iss_sets_explicit_scaled_height_on_uninstall_checkboxes_to_avoid_clipping()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");

    // Chinese captions clip when TNewCheckBox keeps a too-small default Height.
    iss.Should().MatchRegex(
      @"DeleteUserConfigCheckBox\.Height\s*:=\s*ScaleY\(\d+\)");
    iss.Should().MatchRegex(
      @"DeleteUserLogsCheckBox\.Height\s*:=\s*ScaleY\(\d+\)");
  }
}
