using SmartGuard.Adversary;

namespace SmartGuard.Adversary.Tests;

public class AdversaryCliTests
{
  [Theory]
  [InlineData("{\"requestedLayers\":[\"T0\"],\"maxCases\":10}", 40)]
  [InlineData("{\"requestedLayers\":[\"T0\"],\"maxCases\":10,\"trustLevel\":\"Calibrated\"}", 20)]
  [InlineData("{\"requestedLayers\":[99]}", 20)]
  [InlineData("{\"requestedLayers\":[\"T0\",\"T0\"]}", 20)]
  [InlineData("{\"requestedLayers\":[]}", 20)]
  [InlineData("{\"requestedLayers\":[\"T0\"],\"maxCases\":2041}", 20)]
  [InlineData("null", 20)]
  [InlineData("broken", 20)]
  public async Task Development_request_cannot_authorize_work_or_forge_trust(string json, int exit)
  {
    var path = Path.Combine(Path.GetTempPath(), "AdversaryRequest-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
      await File.WriteAllTextAsync(path, json, new System.Text.UTF8Encoding(false));
      using var output = new StringWriter();
      Assert.Equal(exit, await AdversaryCli.RunAsync(["--request", path], output, CancellationToken.None));
      if (exit == 40)
      {
        Assert.Contains("Blocked", output.ToString());
        Assert.DoesNotContain("Passed", output.ToString());
      }
    }
    finally { File.Delete(path); }
  }

  [Fact]
  public async Task Oversized_request_is_rejected_before_json_parsing()
  {
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    try
    {
      await File.WriteAllBytesAsync(path, new byte[65537]);
      Assert.Equal(20, await AdversaryCli.RunAsync(["--request", path], TextWriter.Null, CancellationToken.None));
    }
    finally { File.Delete(path); }
  }
}
