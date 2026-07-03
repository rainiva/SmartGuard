using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class BrandIconLoaderArchitectureTests
{
  [Fact]
  public void Runtime_brand_icon_loading_must_not_read_install_root_icon_files()
  {
    foreach (var file in SourceScanHelper.EnumerateCsFilesUnder("src"))
    {
      var relative = Path.GetRelativePath(SourceScanHelper.RepoRoot, file).Replace('\\', '/');
      if (relative.Contains("/obj/") || relative.Contains("/bin/") || relative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        continue;

      var source = File.ReadAllText(file);
      source.Should().NotContain("\"lib\", \"SmartGuard.ico\"",
        $"embedded-only runtime icon loading forbids install-root icon reads in {relative}");
    }

    var loader = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/BrandIconLoader.cs");
    loader.Should().Contain("GetManifestResourceStream");
    loader.Should().NotContain("SmartGuardPaths.BrandIcon");
    loader.Should().NotContain("File.Exists(");
  }

  [Fact]
  public void TrayIconLoader_and_AppBrandIcon_delegate_to_BrandIconLoader()
  {
    var tray = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Infrastructure.cs");
    tray.Should().Contain("BrandIconLoader");
    tray.Should().NotContain("LoadWinFormsIcon(root");

    var brand = SourceScanHelper.ReadSource("src/SmartGuard.Settings/AppBrandIcon.cs");
    brand.Should().Contain("BrandIconLoader");
    brand.Should().NotContain("installRoot");
  }

  [Fact]
  public void Configuration_project_embeds_canonical_brand_icon_asset()
  {
    var project = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/SmartGuard.Configuration.csproj");
    project.Should().Contain(@"..\..\assets\branding\SmartGuard.ico");
    project.Should().Contain("EmbeddedResource Include=");
  }

  [Fact]
  public void TrayIconLoader_must_not_keep_system_shield_fallback()
  {
    var tray = SourceScanHelper.ReadSource("src/SmartGuard.Tray/Infrastructure.cs");
    tray.Should().Contain("BrandIconLoader.LoadWinFormsIcon()");
    tray.Should().NotContain("SystemIcons.Shield");
  }

  [Fact]
  public void BrandIconLoader_must_not_accept_caller_supplied_fallback_icons()
  {
    var loader = SourceScanHelper.ReadSource("src/SmartGuard.Configuration/BrandIconLoader.cs");
    loader.Should().NotContain("LoadWinFormsIcon(DrawIcon fallback)");
    loader.Should().NotContain("return (DrawIcon)fallback.Clone()");
  }
}
