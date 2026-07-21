using System.Diagnostics;
using SmartGuard.Configuration.Distribution;

namespace SmartGuard.Configuration.Build;

public static class DesktopSuitePublisher
{
  public static IReadOnlyList<string> ProjectRelativePaths { get; } =
    DesktopDistributionManifest.PublishedProjects
      .Select(project => project.ProjectRelativePath)
      .ToList()
      .AsReadOnly();

  internal static Func<ProcessStartInfo, int>? RunProcessForTests;

  public static int Publish(string root, string configuration = "Release")
  {
    root = InstallRootResolver.NormalizeRootPath(root);
    var outputDir = Path.Combine(root, "bin");
    foreach (var project in DesktopDistributionManifest.PublishedProjects)
    {
      var exit = PublishProject(root, project, configuration, outputDir);
      if (exit != 0)
        return exit;
    }

    SystemEventsRidAssetGuard.EnsureAligned(outputDir);
    return 0;
  }

  private static int PublishProject(string root, DesktopPublishedProject project, string configuration, string outputDir)
  {
    var projectPath = Path.Combine(root, project.ProjectRelativePath);
    var arguments = BuildPublishArguments(projectPath, configuration, outputDir, project);
    return RunProcess("dotnet", arguments, root);
  }

  private static string BuildPublishArguments(
    string projectPath,
    string configuration,
    string outputDir,
    DesktopPublishedProject project)
  {
    var args = $"publish \"{projectPath}\" -c {configuration} -r win-x64 --self-contained false -o \"{outputDir}\"";
    if (project.PublishReadyToRun)
      args += " -p:PublishReadyToRun=true";

    return args;
  }

  private static int RunProcess(string fileName, string arguments, string workingDirectory)
  {
    if (RunProcessForTests is not null)
      return RunProcessForTests(new ProcessStartInfo
      {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
      });

    using var process = Process.Start(new ProcessStartInfo
    {
      FileName = fileName,
      Arguments = arguments,
      WorkingDirectory = workingDirectory,
      UseShellExecute = false,
    });
    if (process is null)
      throw new InvalidOperationException($"Failed to start {fileName}");

    process.WaitForExit();
    return process.ExitCode;
  }
}
