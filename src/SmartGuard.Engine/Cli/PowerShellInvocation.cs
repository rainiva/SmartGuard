namespace SmartGuard.Engine.Cli;

public static class PowerShellInvocation
{
  public static string BuildArguments(string scriptPath) =>
    $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";
}
