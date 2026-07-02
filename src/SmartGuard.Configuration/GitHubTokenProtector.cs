using System.Security.Cryptography;
using System.Text;

namespace SmartGuard.Configuration;

public static class GitHubTokenProtector
{
  public const string ProtectedFieldName = "GitHubTokenProtected";
  public const string LegacyFieldName = "GitHubToken";

  public static string? Protect(string? plainText)
  {
    if (string.IsNullOrWhiteSpace(plainText))
      return null;

    var bytes = Encoding.UTF8.GetBytes(plainText.Trim());
    var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
    return Convert.ToBase64String(protectedBytes);
  }

  public static string? TryUnprotect(string? protectedBase64)
  {
    if (string.IsNullOrWhiteSpace(protectedBase64))
      return null;

    try
    {
      var protectedBytes = Convert.FromBase64String(protectedBase64.Trim());
      var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
      return Encoding.UTF8.GetString(bytes);
    }
    catch
    {
      return null;
    }
  }
}
