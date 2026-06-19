using FluentAssertions;
using SmartGuard.Packaging.Versioning;

namespace SmartGuard.Packaging.Tests.Versioning;

public class InstallerVersionResolverTests : IDisposable
{
    private readonly string _tempDir;

    public InstallerVersionResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ReadCurrentVersion_reads_trimmed_content()
    {
        var file = Path.Combine(_tempDir, "version.txt");
        File.WriteAllText(file, "1.0.26\r\n");
        InstallerVersionResolver.ReadCurrentVersion(file).Should().Be("1.0.26");
    }

    [Fact]
    public void ReadCurrentVersion_throws_when_file_missing()
    {
        Action act = () => InstallerVersionResolver.ReadCurrentVersion(Path.Combine(_tempDir, "missing.txt"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void BumpPatchVersion_increments_patch()
    {
        InstallerVersionResolver.BumpPatchVersion("1.0.0").Should().Be("1.0.1");
        InstallerVersionResolver.BumpPatchVersion("1.0.9").Should().Be("1.0.10");
    }

    [Fact]
    public void BumpPatchVersion_rejects_invalid_version()
    {
        Action act = () => InstallerVersionResolver.BumpPatchVersion("not-a-version");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BumpVersionFile_reads_and_writes_next_version()
    {
        var file = Path.Combine(_tempDir, "version.txt");
        File.WriteAllText(file, "2.3.4");
        InstallerVersionResolver.BumpVersionFile(file).Should().Be("2.3.5");
        File.ReadAllText(file).Trim().Should().Be("2.3.5");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
