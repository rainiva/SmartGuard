using SmartGuard.Configuration;

namespace SmartGuard.Engine.Tests;

public class InstallRootResolverTests
{
  [Fact]
  public void Resolve_prefers_explicit_root_argument()
  {
    InstallRootResolver.ResolveFromBaseDirectory(@"C:\Apps\SmartGuard\bin", @"D:\Custom", [])
      .Should().Be(@"D:\Custom");
  }

  [Fact]
  public void Resolve_uses_install_root_when_exe_runs_from_bin_without_config()
  {
    var installRoot = Path.Combine(Path.GetTempPath(), "sg-root-" + Guid.NewGuid().ToString("N"));
    var binDir = Path.Combine(installRoot, "bin");
    Directory.CreateDirectory(binDir);

    try
    {
      InstallRootResolver.ResolveFromBaseDirectory(binDir, null, [])
        .Should().Be(Path.GetFullPath(installRoot));
    }
    finally
    {
      Directory.Delete(installRoot, true);
    }
  }

  [Fact]
  public void Resolve_does_not_fall_back_to_dev_project_path()
  {
    var installRoot = Path.Combine(Path.GetTempPath(), "sg-root-" + Guid.NewGuid().ToString("N"));
    var binDir = Path.Combine(installRoot, "bin");
    Directory.CreateDirectory(binDir);

    try
    {
      var resolved = InstallRootResolver.ResolveFromBaseDirectory(binDir, null, []);
      resolved.Should().NotBe(@"D:\Project\SmartGuard");
      resolved.Should().Be(Path.GetFullPath(installRoot));
    }
    finally
    {
      Directory.Delete(installRoot, true);
    }
  }
}
