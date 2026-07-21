using FluentAssertions;
using SmartGuard.Configuration.Build;
using SmartGuard.Packaging.Staging;

namespace SmartGuard.Packaging.Tests.Staging;

public class StagingLayoutValidatorTests : IDisposable
{
    private readonly string _tempDir;

    public StagingLayoutValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Validate_empty_staging_returns_missing_items()
    {
        var errors = StagingLayoutValidator.Validate(_tempDir);
        errors.Should().Contain("bin\\SmartGuard.Engine.exe");
    }

    [Fact]
    public void Validate_fake_staging_passes()
    {
        FakeStagingBuilder.Build(_tempDir);
        var errors = StagingLayoutValidator.Validate(_tempDir);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_when_SystemEvents_root_hash_mismatches_win_rid_asset()
    {
        FakeStagingBuilder.Build(_tempDir);
        var bin = Path.Combine(_tempDir, "bin");
        var ridDir = Path.Combine(bin, "runtimes", "win", "lib", "net8.0");
        Directory.CreateDirectory(ridDir);
        File.WriteAllBytes(
            Path.Combine(ridDir, "Microsoft.Win32.SystemEvents.dll"),
            [1, 2, 3, 4, 5, 6]);
        File.WriteAllBytes(
            Path.Combine(bin, "Microsoft.Win32.SystemEvents.dll"),
            [9, 9, 9]);

        var errors = StagingLayoutValidator.Validate(_tempDir);

        errors.Should().Contain(error =>
            error.Contains("Microsoft.Win32.SystemEvents.dll", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
