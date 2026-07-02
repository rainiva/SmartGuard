using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class BrandIconLoaderArchitectureTests
{
  [Fact]
  public void Only_BrandIconLoader_may_load_lib_SmartGuard_ico_from_install_root()
  {
    var allowedPathLiterals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "src/SmartGuard.Configuration/BrandIconLoader.cs",
      "src/SmartGuard.Configuration/SmartGuardPaths.cs",
    };

    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      var relative = Path.GetRelativePath(SourceScanHelper.RepoRoot, file).Replace('\\', '/');
      if (relative.Contains("/obj/") || relative.Contains("/bin/") || relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        continue;
      if (relative.StartsWith("src/SmartGuard.Packaging/", StringComparison.OrdinalIgnoreCase))
        continue;

      var source = File.ReadAllText(file);
      if (!source.Contains("\"lib\", \"SmartGuard.ico\""))
        continue;

      allowedPathLiterals.Should().Contain(relative,
        $"install-root brand icon path literals must use SmartGuardPaths.BrandIcon, not {relative}");
    }
  }

  [Fact]
  public void TrayIconLoader_and_AppBrandIcon_delegate_to_BrandIconLoader()
  {
    var tray = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Infrastructure.cs");
    tray.Should().Contain("BrandIconLoader");

    var brand = SourceScanHelper.ReadSource("src/SmartGuard.Settings/AppBrandIcon.cs");
    brand.Should().Contain("BrandIconLoader");
  }

  [Fact]
  public void BrandIconLoader_must_use_taskbar_frame_size_constant_not_parameterless_icon_ctor()
  {
    var source = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/BrandIconLoader.cs");
    source.Should().Contain("TaskbarIconFrameSize",
      "WinForms and WPF icon paths must share one taskbar frame size constant");
    source.Should().NotMatchRegex(
      @"new\s+DrawIcon\s*\(\s*iconPath\s*\)",
      "LoadWinFormsIcon must not use parameterless Icon(path); specify TaskbarIconFrameSize");
    source.Should().NotMatchRegex(
      @"new\s+DrawIcon\s*\(\s*filePath\s*,\s*32\s*,\s*32\s*\)",
      "WPF path must use TaskbarIconFrameSize constant instead of literal 32");
  }
}
