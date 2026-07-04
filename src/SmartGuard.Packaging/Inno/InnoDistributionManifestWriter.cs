using System.Text;
using SmartGuard.Configuration;
using SmartGuard.Configuration.Distribution;

namespace SmartGuard.Packaging.Inno;

internal static class InnoDistributionManifestWriter
{
  private static readonly UTF8Encoding Utf8NoBom = new(false);

  internal static void Write(string root)
  {
    var generatedDir = Path.Combine(root, "installer", "generated");
    Directory.CreateDirectory(generatedDir);

    File.WriteAllText(
      Path.Combine(generatedDir, "SmartGuard.Distribution.Constants.iss.inc"),
      BuildConstantsInclude(),
      Utf8NoBom);
    File.WriteAllText(
      Path.Combine(generatedDir, "SmartGuard.Distribution.Run.iss.inc"),
      BuildRunInclude(),
      Utf8NoBom);
    File.WriteAllText(
      Path.Combine(generatedDir, "SmartGuard.Distribution.Files.iss.inc"),
      BuildFilesInclude(),
      Utf8NoBom);
    File.WriteAllText(
      Path.Combine(generatedDir, "SmartGuard.Distribution.Processes.iss.inc"),
      BuildProcessesInclude(),
      Utf8NoBom);
    File.WriteAllText(
      Path.Combine(generatedDir, "SmartGuard.Distribution.LegacyCleanup.iss.inc"),
      BuildLegacyCleanupInclude(),
      Utf8NoBom);
  }

  private static string BuildConstantsInclude()
  {
    return string.Join(Environment.NewLine, new[]
    {
      $@"#define EngineExeName ""{SmartGuardPaths.EngineExeFileName}""",
      $@"#define TrayExeName ""{SmartGuardPaths.TrayExeFileName}""",
      $@"#define SettingsExeName ""{SmartGuardPaths.SettingsExeFileName}""",
      $@"#define CliExeName ""{SmartGuardPaths.CliExeFileName}""",
      $@"#define GuardianTaskName ""{ScheduledTaskRegistrar.GuardianTaskName}""",
    }) + Environment.NewLine;
  }

  private static string BuildRunInclude()
  {
    return string.Join(Environment.NewLine, new[]
    {
      @"Filename: ""{tmp}\{#RuntimeInstallerFile}""; Parameters: ""/install /quiet /norestart""; StatusMsg: ""正在安装 .NET 8 桌面运行时...""; Check: ShouldInstallDotNet; Flags: waituntilterminated",
      @"Filename: ""{app}\bin\{#EngineExeName}""; Parameters: ""--root """"{app}"""" --install --skip-publish""; StatusMsg: ""正在注册计划任务...""; Check: not WizardIsTaskSelected('desktopicon'); Flags: runhidden waituntilterminated",
      @"Filename: ""{app}\bin\{#EngineExeName}""; Parameters: ""--root """"{app}"""" --install --skip-publish --create-desktop-shortcut""; StatusMsg: ""正在注册计划任务...""; Tasks: desktopicon; Flags: runhidden waituntilterminated",
      @"Filename: ""{sys}\schtasks.exe""; Parameters: ""/Run /TN """"{#GuardianTaskName}""""""; StatusMsg: ""正在启动核心服务...""; Flags: runhidden waituntilterminated",
      @"Filename: ""{app}\bin\{#CliExeName}""; Parameters: ""open tray --root """"{app}""""""; Description: ""启动 {#MyAppName} 托盘""; Flags: runhidden nowait postinstall skipifsilent; Tasks: launchtray",
    }) + Environment.NewLine;
  }

  private static string BuildFilesInclude()
  {
    var lines = new List<string>
    {
      @"Source: ""{#StagingDir}\bin\*""; DestDir: ""{app}\bin""; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace",
    };

    lines.AddRange(DesktopDistributionManifest.PayloadEntries.Select(entry =>
    {
      var destinationDir = Path.GetDirectoryName(entry.DestinationRelativePath)?.Replace('/', '\\');
      var destDir = string.IsNullOrWhiteSpace(destinationDir)
        ? @"{app}"
        : $@"{{app}}\{destinationDir}";
      return $@"Source: ""{{#StagingDir}}\{entry.DestinationRelativePath.Replace('/', '\\')}""; DestDir: ""{destDir}""; Flags: ignoreversion skipifsourcedoesntexist";
    }));

    return string.Join(Environment.NewLine, lines) + Environment.NewLine;
  }

  private static string BuildProcessesInclude()
  {
    if (DesktopDistributionManifest.InstallerProcesses.Count == 0)
      return "        False;" + Environment.NewLine;

    var builder = new StringBuilder();
    for (var index = 0; index < DesktopDistributionManifest.InstallerProcesses.Count; index++)
    {
      var suffix = index == DesktopDistributionManifest.InstallerProcesses.Count - 1 ? ";" : " or";
      builder.Append("        (Pos('");
      builder.Append(DesktopDistributionManifest.InstallerProcesses[index]);
      builder.Append("', Output) > 0)");
      builder.Append(suffix);
      builder.AppendLine();
    }

    return builder.ToString();
  }

  private static string BuildLegacyCleanupInclude()
  {
    return string.Join(
             Environment.NewLine,
             DesktopDistributionManifest.LegacyCleanupPaths.Select(path =>
               $@"Type: filesandordirs; Name: ""{{app}}\{path.Replace('/', '\\')}""")) +
           Environment.NewLine;
  }
}
