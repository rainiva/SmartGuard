using System.Runtime.CompilerServices;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Tests;

internal static class PowerWriteGuard
{
  internal const string Variable = "SMARTGUARD_POWER_WRITE_TESTS";
  internal static bool WritesRequested => Environment.GetEnvironmentVariable(Variable) == "1";

  // Tests read real power settings only: a real SetActive/WriteValueIndex throws unless the user opted in.
  [ModuleInitializer]
  internal static void Install() => PowerSchemeService.RealWritesAllowed = WritesRequested;
}
