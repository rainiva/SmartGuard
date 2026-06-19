using FluentAssertions;
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

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
