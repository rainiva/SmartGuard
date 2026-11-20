using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("ConfigAtomicWriteTests")]
public class GuardConfigRepositoryAtomicWriteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_publication_preserves_previous_config_and_allows_retry(bool manualMutation)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardRepoAtomic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");
        var repo = new GuardConfigRepository(path);
        var config = GuardConfig.CreateDefault(dir);
        try
        {
            repo.Save(config);
            var previousBytes = File.ReadAllBytes(path);
            var until = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            config.CheckIntervalSec = 21;
            Action save = manualMutation
                ? () => repo.SetManualHighPerformanceUntil(until)
                : () => repo.Save(config);

            // Losing the staged file must not remove the last published policy.
            GuardConfigAtomicFileWriter.BeforeMoveForTests = (temp, _) => File.Delete(temp);
            save.Should().Throw<IOException>();
            File.Exists(path).Should().BeTrue("a failed publication must preserve the last valid configuration");
            File.ReadAllBytes(path).Should().Equal(previousBytes);

            GuardConfigAtomicFileWriter.BeforeMoveForTests = null;
            save();
            var loaded = new GuardConfigRepository(path).TryLoad();
            loaded.Should().NotBeNull();
            if (manualMutation)
                loaded!.ManualHighPerformanceUntil.Should().Be(until);
            else
                loaded!.CheckIntervalSec.Should().Be(21);
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally
        {
            GuardConfigAtomicFileWriter.BeforeMoveForTests = null;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Locked_staged_file_preserves_previous_config_and_allows_retry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardRepoAtomic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");
        var repo = new GuardConfigRepository(path);
        var config = GuardConfig.CreateDefault(dir);
        FileStream? stagedLock = null;
        try
        {
            repo.Save(config);
            var previousBytes = File.ReadAllBytes(path);
            config.CheckIntervalSec = 22;
            // Exercise the real Windows publication API's sharing failure, not a mocked exception.
            GuardConfigAtomicFileWriter.BeforeMoveForTests = (temp, _) =>
                stagedLock = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None);
            Action save = () => repo.Save(config);
            save.Should().Throw<IOException>();
            File.Exists(path).Should().BeTrue();
            File.ReadAllBytes(path).Should().Equal(previousBytes);
            new GuardConfigRepository(path).TryLoad()!.CheckIntervalSec.Should().NotBe(22);

            stagedLock!.Dispose();
            stagedLock = null;
            GuardConfigAtomicFileWriter.BeforeMoveForTests = null;
            save();
            new GuardConfigRepository(path).TryLoad()!.CheckIntervalSec.Should().Be(22);
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally
        {
            stagedLock?.Dispose();
            GuardConfigAtomicFileWriter.BeforeMoveForTests = null;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Save_uses_atomic_file_writer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardRepoAtomic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");
        var usedAtomicWriter = false;

        try
        {
            GuardConfigAtomicFileWriter.BeforeMoveForTests = (_, _) => usedAtomicWriter = true;

            var repo = new GuardConfigRepository(path);
            var config = GuardConfig.CreateDefault(dir);
            config.CheckIntervalSec = 20;
            repo.Save(config);

            usedAtomicWriter.Should().BeTrue();
        }
        finally
        {
            GuardConfigAtomicFileWriter.BeforeMoveForTests = null;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
