using System.Text;
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
