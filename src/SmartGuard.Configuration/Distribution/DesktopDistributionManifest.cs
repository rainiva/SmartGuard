namespace SmartGuard.Configuration.Distribution;

public sealed record DesktopPublishedProject(
  string ProjectRelativePath,
  string OutputFileName,
  bool PublishReadyToRun = false,
  bool IncludeInInstallerProcessList = true);

public sealed record DesktopPayloadEntry(
  string SourceRelativePath,
  string DestinationRelativePath);

public static class DesktopDistributionManifest
{
  public static IReadOnlyList<DesktopPublishedProject> PublishedProjects { get; } =
    new List<DesktopPublishedProject>
    {
      new(@"src/SmartGuard.Engine/SmartGuard.Engine.csproj", SmartGuardPaths.EngineExeFileName),
      new(@"src/SmartGuard.Tray/SmartGuard.Tray.csproj", SmartGuardPaths.TrayExeFileName),
      new(@"src/SmartGuard.Settings/SmartGuard.Settings.csproj", SmartGuardPaths.SettingsExeFileName, PublishReadyToRun: true),
      new(@"src/SmartGuard.Cli/SmartGuard.Cli.csproj", SmartGuardPaths.CliExeFileName, IncludeInInstallerProcessList: false),
    }.AsReadOnly();

  public static IReadOnlyList<DesktopPayloadEntry> PayloadEntries { get; } =
    new List<DesktopPayloadEntry>
    {
      new(@"installer/license_zh-CN.txt", @"license_zh-CN.txt"),
    }.AsReadOnly();

  public static IReadOnlyList<string> InstallerProcesses { get; } =
    PublishedProjects
      .Where(project => project.IncludeInInstallerProcessList)
      .Select(project => project.OutputFileName)
      .ToList()
      .AsReadOnly();

  public static IReadOnlyList<string> LegacyCleanupPaths { get; } =
    new List<string>
    {
      @"lib",
      @"bin\SmartGuard.LogViewer.exe",
      @"bin\SmartGuard.LogViewer.dll",
      @"bin\SmartGuard.LogViewer.deps.json",
      @"bin\SmartGuard.LogViewer.runtimeconfig.json",
      @"bin\SmartGuard.LogViewer.pdb",
    }.AsReadOnly();

  public static IReadOnlyList<string> RequiredStagingPaths { get; } =
    BuildRequiredStagingPaths();

  private static IReadOnlyList<string> BuildRequiredStagingPaths()
  {
    return PublishedProjects
      .Select(project => $@"bin\{project.OutputFileName}")
      .Concat(PayloadEntries.Select(entry => entry.DestinationRelativePath))
      .Append(@"VERSION.txt")
      .ToList()
      .AsReadOnly();
  }
}
