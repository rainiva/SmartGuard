using System.Text.Json;

namespace SmartGuard.Settings.Tests;

public class UpdateInstallerLauncherTests
{
    [Fact]
    public void ResolveAsset_finds_matching_installer_asset()
    {
        var json = @"{
            ""tag_name"": ""v1.0.29"",
            ""assets"": [
                { ""name"": ""SmartGuard-Setup-1.0.29-x64.exe"", ""browser_download_url"": ""https://example.com/dl.exe"" },
                { ""name"": ""source.zip"", ""browser_download_url"": ""https://example.com/src.zip"" }
            ]
        }";

        using var doc = JsonDocument.Parse(json);
        var (name, url) = UpdateInstallerLauncher.ResolveAsset(doc.RootElement, new Version(1, 0, 29));

        name.Should().Be("SmartGuard-Setup-1.0.29-x64.exe");
        url.Should().Be("https://example.com/dl.exe");
    }

    [Fact]
    public void ResolveAsset_returns_null_when_no_matching_asset()
    {
        var json = @"{
            ""tag_name"": ""v1.0.29"",
            ""assets"": [
                { ""name"": ""source.zip"", ""browser_download_url"": ""https://example.com/src.zip"" }
            ]
        }";

        using var doc = JsonDocument.Parse(json);
        var (name, url) = UpdateInstallerLauncher.ResolveAsset(doc.RootElement, new Version(1, 0, 29));

        name.Should().BeNull();
        url.Should().BeNull();
    }

    [Fact]
    public void ResolveAsset_returns_null_when_assets_missing()
    {
        var json = @"{ ""tag_name"": ""v1.0.29"" }";

        using var doc = JsonDocument.Parse(json);
        var (name, url) = UpdateInstallerLauncher.ResolveAsset(doc.RootElement, new Version(1, 0, 29));

        name.Should().BeNull();
        url.Should().BeNull();
    }

    [Fact]
    public void GetLocalInstallerPath_uses_temp_directory_and_asset_name()
    {
        var path = UpdateInstallerLauncher.GetLocalInstallerPath("SmartGuard-Setup-1.0.29-x64.exe");

        Path.GetDirectoryName(path).Should().Be(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        Path.GetFileName(path).Should().Be("SmartGuard-Setup-1.0.29-x64.exe");
    }

    [Fact]
    public void ResolveAsset_ignores_assets_without_download_url()
    {
        var json = @"{
            ""tag_name"": ""v1.0.29"",
            ""assets"": [
                { ""name"": ""SmartGuard-Setup-1.0.29-x64.exe"" }
            ]
        }";

        using var doc = JsonDocument.Parse(json);
        var (name, url) = UpdateInstallerLauncher.ResolveAsset(doc.RootElement, new Version(1, 0, 29));

        name.Should().BeNull();
        url.Should().BeNull();
    }
}
