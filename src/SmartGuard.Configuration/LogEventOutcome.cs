using System.Text.Json;

namespace SmartGuard.Configuration;

public static class LogEventOutcome
{
  public const string PropertyName = "outcome";
  public const string Handled = "handled";

  public static IReadOnlyDictionary<string, JsonElement> CreateProperties(string outcome)
  {
    if (!string.Equals(outcome, Handled, StringComparison.Ordinal))
      throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown log event outcome.");

    return new Dictionary<string, JsonElement>
    {
      [PropertyName] = JsonSerializer.SerializeToElement(outcome),
    };
  }
}
