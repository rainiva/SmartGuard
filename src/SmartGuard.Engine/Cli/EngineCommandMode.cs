namespace SmartGuard.Engine.Cli;

public enum EngineCommandMode
{
  RunGuardian,
  Install,
  Uninstall,
}

public sealed record ParsedCommandLine(
  EngineCommandMode Mode,
  string? Root,
  bool SkipPublish);
