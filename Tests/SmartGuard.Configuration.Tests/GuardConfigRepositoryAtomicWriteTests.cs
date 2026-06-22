using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

[Collection("ConfigAtomicWriteTests")]
public class GuardConfigRepositoryAtomicWriteTests
{
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
