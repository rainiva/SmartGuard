using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SmartGuard.Configuration;

public static partial class DiagnosticRedactor
{
  public const string RedactedUserSegment = "[REDACTED]";
  public const string RedactedSecretValue = "[REDACTED]";

  [GeneratedRegex(@"\\Users\\[^\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex UserProfilePathRegex();

  [GeneratedRegex(@"(?<prefix>(?:--?(?:token|password|secret)(?:=|\s+))|\b(?:token|password|secret)=)(?<value>\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex CommandSecretRegex();

  private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
  {
    "token",
    "password",
    "secret",
    "apikey",
    "api_key",
    "authorization",
  };

  public static string RedactText(string text)
  {
    if (string.IsNullOrEmpty(text))
      return text;

    var redacted = UserProfilePathRegex().Replace(text, $"\\Users\\{RedactedUserSegment}");
    redacted = CommandSecretRegex().Replace(redacted, m => m.Groups["prefix"].Value + RedactedSecretValue);
    return redacted;
  }

  public static string RedactConfigJson(string configJson)
  {
    var node = JsonNode.Parse(configJson);
    if (node is not JsonObject obj)
      return RedactText(configJson);

    obj.Remove(GitHubTokenProtector.LegacyFieldName);
    obj.Remove(GitHubTokenProtector.ProtectedFieldName);
    RedactJsonObjectSecrets(obj);
    return RedactText(obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
  }

  public static LogEvent RedactEvent(LogEvent logEvent)
  {
    var message = RedactText(logEvent.Message);
    var properties = RedactProperties(logEvent.Properties);
    return logEvent with { Message = message, Properties = properties };
  }

  internal static string SanitizeConfigJson(string configJson) => RedactConfigJson(configJson);

  private static IReadOnlyDictionary<string, JsonElement> RedactProperties(
    IReadOnlyDictionary<string, JsonElement> properties)
  {
    if (properties.Count == 0)
      return properties;

    var redacted = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    foreach (var (key, value) in properties)
    {
      if (SensitivePropertyNames.Contains(key))
      {
        redacted[key] = JsonSerializer.SerializeToElement(RedactedSecretValue);
        continue;
      }

      if (value.ValueKind == JsonValueKind.String)
      {
        var text = RedactText(value.GetString() ?? string.Empty);
        redacted[key] = JsonSerializer.SerializeToElement(text);
        continue;
      }

      redacted[key] = value.Clone();
    }

    return redacted;
  }

  private static void RedactJsonObjectSecrets(JsonObject obj)
  {
    foreach (var property in obj.ToList())
    {
      if (property.Value is null)
        continue;

      if (SensitivePropertyNames.Contains(property.Key))
      {
        obj[property.Key] = RedactedSecretValue;
        continue;
      }

      if (property.Value is JsonObject childObject)
        RedactJsonObjectSecrets(childObject);
      else if (property.Value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
        obj[property.Key] = RedactText(text);
    }
  }
}
