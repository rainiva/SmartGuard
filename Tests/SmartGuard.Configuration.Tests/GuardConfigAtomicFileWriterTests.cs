using FluentAssertions;
using SmartGuard.Configuration;
using System.Text.Json.Nodes;

namespace SmartGuard.Configuration.Tests;

[Collection("ConfigAtomicWriteTests")]
public class GuardConfigAtomicFileWriterTests
{
    [Fact]
    public void WriteAllText_writes_complete_json_to_temp_before_replace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmartGuardAtomicWrite_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SmartGuard.config.json");
        var observedBeforeMove = false;

        try
        {
            GuardConfigAtomicFileWriter.BeforeMoveForTests = (tempPath, finalPath) =>
            {
                observedBeforeMove = true;
                tempPath.Should().Be(finalPath + ".tmp");
                File.Exists(tempPath).Should().BeTrue();
                var tempContent = File.ReadAllText(tempPath);
                tempContent.Should().Contain("\"CheckIntervalSec\"");
                JsonNode.Parse(tempContent).Should().NotBeNull();
            };

            GuardConfigAtomicFileWriter.WriteAllText(path, """
                {
                  "CheckIntervalSec": 15
                }
                """);

            observedBeforeMove.Should().BeTrue();
            File.Exists(path + ".tmp").Should().BeFalse();
            JsonNode.Parse(File.ReadAllText(path)).Should().NotBeNull();
        }
        finally
        {
            GuardConfigAtomicFileWriter.BeforeMoveForTests = null;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
