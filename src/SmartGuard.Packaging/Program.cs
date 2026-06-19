using SmartGuard.Packaging.Commands;

namespace SmartGuard.Packaging;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var root = Directory.GetCurrentDirectory();
        var configuration = "Release";
        var stagingDir = "";
        var runtimeVersion = "8.0.18";
        string? isccPath = null;
        bool skipPublish = false;
        bool skipRedist = false;
        bool skipBump = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-c":
                case "--configuration":
                    configuration = args[++i];
                    break;
                case "-s":
                case "--staging-dir":
                    stagingDir = args[++i];
                    break;
                case "-r":
                case "--runtime-version":
                    runtimeVersion = args[++i];
                    break;
                case "--iscc":
                    isccPath = args[++i];
                    break;
                case "--root":
                    root = args[++i];
                    break;
                case "--skip-publish":
                    skipPublish = true;
                    break;
                case "--skip-redist":
                    skipRedist = true;
                    break;
                case "--skip-version-bump":
                    skipBump = true;
                    break;
                default:
                    PrintUsage();
                    return 1;
            }
        }

        return args[0].ToLowerInvariant() switch
        {
            "stage" => new StageCommand().Run(new StageOptions(root, configuration, stagingDir, runtimeVersion, skipPublish, skipRedist)),
            "installer" => new BuildInstallerCommand().Run(new BuildInstallerOptions(root, configuration, stagingDir, isccPath, skipPublish, skipRedist, skipBump, runtimeVersion)),
            _ => PrintUsage()
        };
    }

    static int PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  SmartGuard.Packaging stage [--root <dir>] [--configuration <cfg>] [--staging-dir <dir>] [--runtime-version <ver>] [--skip-publish] [--skip-redist]");
        Console.WriteLine("  SmartGuard.Packaging installer [--root <dir>] [--configuration <cfg>] [--staging-dir <dir>] [--runtime-version <ver>] [--iscc <path>] [--skip-publish] [--skip-redist] [--skip-version-bump]");
        return 1;
    }
}
