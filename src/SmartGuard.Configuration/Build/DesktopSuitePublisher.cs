using System.Diagnostics;

namespace SmartGuard.Configuration.Build;

public static class DesktopSuitePublisher
{
  public static IReadOnlyList<string> ProjectRelativePaths { get; } =
  [
    "src/SmartGuard.Engine/SmartGuard.Engine.csproj",
    "src/SmartGuard.Tray/SmartGuard.Tray.csproj",
    "src/SmartGuard.LogViewer/SmartGuard.LogViewer.csproj",
    "src/SmartGuard.Settings/SmartGuard.Settings.csproj",
    "src/SmartGuard.Cli/SmartGuard.Cli.csproj",
  ];

  internal static Func<ProcessStartInfo, int>? RunProcessForTests;

  public static int Publish(string root, string configuration = "Release")
  {
    var outputDir = Path.Combine(root, "bin");
    foreach (var relativeProject in ProjectRelativePaths)
    {
      var exit = PublishProject(root, relativeProject, configuration, outputDir);
      if (exit != 0)
        return exit;
    }

    return 0;
  }

  private static int PublishProject(string root, string relativeProject, string configuration, string outputDir)
  {
    var projectPath = Path.Combine(root, relativeProject);
    var arguments = BuildPublishArguments(projectPath, configuration, outputDir, relativeProject);
    return RunProcess("dotnet", arguments, root);
  }

  private static string BuildPublishArguments(
    string projectPath,
    string configuration,
    string outputDir,
    string relativeProject)
  {
    var args = $"publish \"{projectPath}\" -c {configuration} -r win-x64 --self-contained false -o \"{outputDir}\"";
    if (relativeProject.Contains("SmartGuard.LogViewer", StringComparison.Ordinal)
        || relativeProject.Contains("SmartGuard.Settings", StringComparison.Ordinal))
    {
      args += " -p:PublishReadyToRun=true";
    }

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
