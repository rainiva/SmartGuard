namespace SmartGuard.Configuration;

public static class SmartGuardPaths
{
  public const string ConfigFileName = "SmartGuard.config.json";
  public const string StatusFileName = "SmartGuard.status.json";
  public const string DefaultLogFileName = "SmartGuard.log";
  public const string StartupLogFileName = "SmartGuard.startup.log";
  public const string EngineExeFileName = "SmartGuard.Engine.exe";
  public const string TrayExeFileName = "SmartGuard.Tray.exe";
  public const string SettingsExeFileName = "SmartGuard.Settings.exe";
  public const string CliExeFileName = "SmartGuard.Cli.exe";
  public const string InitMarkerFileName = ".SmartGuard.initialized";
  public const string ElevationDeclinedMarkerFileName = ".SmartGuard.elevation-declined";
  public const string BrandIconFileName = "SmartGuard.ico";

  public static IReadOnlyList<string> ProcessImageNames { get; } =
  [
    TrayExeFileName,
    EngineExeFileName,
    SettingsExeFileName,
  ];

  public static string ConfigFile(string root) => Path.Combine(root, ConfigFileName);
  public static string StatusFile(string root) => Path.Combine(root, StatusFileName);
  public static string DefaultLogFile(string root) => Path.Combine(root, DefaultLogFileName);
  public static string StartupLogFile(string root) => Path.Combine(root, StartupLogFileName);
  public static string EngineExe(string root) => Path.Combine(root, "bin", EngineExeFileName);
  public static string TrayExe(string root) => Path.Combine(root, "bin", TrayExeFileName);
  public static string SettingsExe(string root) => Path.Combine(root, "bin", SettingsExeFileName);
  public static string CliExe(string root) => Path.Combine(root, "bin", CliExeFileName);
  public static string InitMarkerFile(string root) => Path.Combine(root, InitMarkerFileName);
  public static string ElevationDeclinedMarkerFile(string root) => Path.Combine(root, ElevationDeclinedMarkerFileName);
  public static string BrandIconFile(string root) => Path.Combine(root, "bin", BrandIconFileName);

  public static string ResolveLogFile(GuardConfig config, string root)
    => string.IsNullOrWhiteSpace(config.LogFile) ? DefaultLogFile(root) : config.LogFile;
}
