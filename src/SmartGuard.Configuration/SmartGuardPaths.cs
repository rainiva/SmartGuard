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
  public const string LogViewerExeFileName = "SmartGuard.LogViewer.exe";
  public const string SettingsXamlFileName = "SmartGuard.Settings.xaml";
  public const string InitMarkerFileName = ".SmartGuard.initialized";
  public const string ElevationDeclinedMarkerFileName = ".SmartGuard.elevation-declined";

  public static IReadOnlyList<string> ProcessImageNames { get; } =
  [
    TrayExeFileName,
    EngineExeFileName,
    LogViewerExeFileName,
    SettingsExeFileName,
  ];

  public static string ConfigFile(string root) => Path.Combine(root, ConfigFileName);
  public static string StatusFile(string root) => Path.Combine(root, StatusFileName);
  public static string DefaultLogFile(string root) => Path.Combine(root, DefaultLogFileName);
  public static string StartupLogFile(string root) => Path.Combine(root, StartupLogFileName);
  public static string EngineExe(string root) => Path.Combine(root, "bin", EngineExeFileName);
  public static string TrayExe(string root) => Path.Combine(root, "bin", TrayExeFileName);
  public static string SettingsExe(string root) => Path.Combine(root, "bin", SettingsExeFileName);
  public static string LogViewerExe(string root) => Path.Combine(root, "bin", LogViewerExeFileName);
  public static string BrandIcon(string root) => Path.Combine(root, "lib", "SmartGuard.ico");
  public static string SettingsXaml(string root) => Path.Combine(root, "lib", SettingsXamlFileName);
  public static string InitMarkerFile(string root) => Path.Combine(root, InitMarkerFileName);
  public static string ElevationDeclinedMarkerFile(string root) => Path.Combine(root, ElevationDeclinedMarkerFileName);

  public static string ResolveLogFile(GuardConfig config, string root)
    => string.IsNullOrWhiteSpace(config.LogFile) ? DefaultLogFile(root) : config.LogFile;
}
