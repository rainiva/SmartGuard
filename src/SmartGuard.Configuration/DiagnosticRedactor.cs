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
      throw new JsonException("Diagnostic configuration requires an object root.");

    RetiredSecretFieldRegistry.RemoveFrom(obj);
    RedactSavedLogFilters(obj);
    RedactJsonNode(obj, 0);
    return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
  }

  public static string RedactJson(string json)
    => RedactJsonNode(JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }), 0)!
      .ToJsonString(new JsonSerializerOptions { WriteIndented = true });

  public static LogEvent RedactEvent(LogEvent logEvent)
  {
    var message = RedactText(logEvent.Message);
    var properties = RedactProperties(logEvent.Properties);
    return logEvent with { Message = message, Properties = properties, Exception = RedactException(logEvent.Exception, 0) };
  }

  private static LogExceptionInfo? RedactException(LogExceptionInfo? exception, int depth)
  {
    if (exception is null) return null;
    if (depth >= 64) throw new JsonException("Diagnostic exception depth exceeded.");
    return exception with { Type = RedactText(exception.Type), Message = RedactText(exception.Message),
      StackTrace = exception.StackTrace is null ? null : RedactText(exception.StackTrace), Inner = RedactException(exception.Inner, depth + 1) };
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

      var node = JsonNode.Parse(value.GetRawText(), documentOptions: new JsonDocumentOptions { MaxDepth = 64 });
      redacted[key] = JsonSerializer.SerializeToElement(RedactJsonNode(node, 0));
    }

    return redacted;
  }

  private static JsonNode? RedactJsonNode(JsonNode? node, int depth)
  {
    if (depth > 64) throw new JsonException("Diagnostic JSON depth exceeded.");
    if (node is JsonObject obj)
    {
      foreach (var property in obj.ToList())
      {
        var replacement = SensitivePropertyNames.Contains(property.Key) ? JsonValue.Create(RedactedSecretValue)
          : RedactJsonNode(property.Value, depth + 1);
        if (!ReferenceEquals(replacement, property.Value)) obj[property.Key] = replacement;
      }
    }
    else if (node is JsonArray array)
      for (var i = 0; i < array.Count; i++)
      {
        // Objects/arrays are mutated under their existing parent. Reassigning
        // the same child to JsonArray violates its single-parent ownership.
        var original = array[i]; var replacement = RedactJsonNode(original, depth + 1);
        if (!ReferenceEquals(original, replacement)) array[i] = replacement;
      }
    else if (node is JsonValue value && value.TryGetValue<string>(out var text)) return JsonValue.Create(RedactText(text));
    return node;
  }

  private static void RedactSavedLogFilters(JsonObject configNode)
  {
    if (configNode["LogViewPreferences"] is not JsonObject prefsNode)
      return;

    if (prefsNode["DefaultFilterId"] is JsonValue defaultFilterId)
      prefsNode["DefaultFilterId"] = RedactedSecretValue;

    if (prefsNode["SavedFilters"] is not JsonArray savedFilters)
      return;

    foreach (var entry in savedFilters)
    {
      if (entry is not JsonObject filterObject)
        continue;

      if (filterObject["Name"] is JsonValue)
        filterObject["Name"] = RedactedSecretValue;

      if (filterObject["Query"] is JsonObject queryObject)
        RedactSavedFilterQuery(queryObject);
    }
  }

  private static void RedactSavedFilterQuery(JsonObject queryObject)
  {
    if (queryObject["Value"] is JsonValue)
      queryObject["Value"] = RedactedSecretValue;

    foreach (var property in queryObject.ToList())
    {
      if (property.Value is JsonObject childObject)
        RedactSavedFilterQuery(childObject);
    }
  }
}
