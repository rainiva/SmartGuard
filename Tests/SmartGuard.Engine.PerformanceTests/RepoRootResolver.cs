namespace SmartGuard.Engine.PerformanceTests;

public static class RepoRootResolver
{
    public static string Resolve()
    {
        var current = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SmartGuard.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not resolve repo root.");
    }
}
