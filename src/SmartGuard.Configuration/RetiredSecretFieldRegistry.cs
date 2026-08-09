using System.Text.Json.Nodes;

namespace SmartGuard.Configuration;

internal static class RetiredSecretFieldRegistry
{
  internal const string LegacyPlainText = "GitHubToken";
  internal const string LegacyProtected = "GitHubTokenProtected";

  internal static bool RemoveFrom(JsonObject node)
  {
    var removedPlainText = node.Remove(LegacyPlainText);
    var removedProtected = node.Remove(LegacyProtected);
    return removedPlainText || removedProtected;
  }
}
