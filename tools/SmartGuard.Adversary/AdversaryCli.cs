using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartGuard.Adversary;

public static class AdversaryCli
{
  public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
  {
    try
    {
      if (args.Length != 2 || args[0] != "--request") throw new ArgumentException("Expected --request <development request file>.");
      // One extra byte detects oversize even if the file grows after opening.
      var bytes = new byte[65537];
      using var stream = new FileStream(args[1], FileMode.Open, FileAccess.Read, FileShare.Read);
      var count = 0;
      while (count < bytes.Length)
      {
        var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken);
        if (read == 0) break;
        count += read;
      }
      if (count > 65536) throw new ArgumentException("Request exceeds 64KiB.");
      var options = new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
      };
      var request = JsonSerializer.Deserialize<AdversaryRequest>(new UTF8Encoding(false, true).GetString(bytes, 0, count), options)
        ?? throw new ArgumentException("Request is null.");
      var observation = await AdversaryCommand.RunAsync(request, cancellationToken);
      await output.WriteLineAsync(JsonSerializer.Serialize(observation, options));
      await output.WriteLineAsync("Blocked: development skeleton only; no attack was executed or calibrated.");
      return 40;
    }
    catch (OperationCanceledException) { return 41; }
    catch (Exception error) when (error is ArgumentException or JsonException or IOException or UnauthorizedAccessException)
    {
      // Do not echo the request, exception text or user paths into durable output.
      await output.WriteLineAsync("InfrastructureFailed: invalid or unreadable bounded development request.");
      return 20;
    }
  }
}
