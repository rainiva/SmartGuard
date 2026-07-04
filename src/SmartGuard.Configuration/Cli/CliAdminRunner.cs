using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace SmartGuard.Configuration.Cli;

public static class CliAdminRunner
{
  public static int RunElevatedIfNeeded(IReadOnlyList<string> args, Func<int> action)
  {
    if (IsAdministrator())
      return action();

    var quoted = string.Join(" ", args.Select(QuoteArgument));
    using var proc = ElevationHelper.RerunCurrentProcessElevated(quoted);
    if (proc is null)
      return 1;

    proc.WaitForExit();
    return proc.ExitCode;
  }

  private static bool IsAdministrator()
  {
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
  }

  private static string QuoteArgument(string value)
  {
    if (string.IsNullOrEmpty(value))
      return "\"\"";

    if (!value.Contains(' ') && !value.Contains('"'))
      return value;

    var builder = new StringBuilder();
    builder.Append('"');
    foreach (var ch in value)
    {
      if (ch == '"')
        builder.Append('\\');
      builder.Append(ch);
    }
    builder.Append('"');
    return builder.ToString();
  }
}
