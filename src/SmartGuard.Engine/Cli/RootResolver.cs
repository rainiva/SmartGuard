namespace SmartGuard.Engine.Cli;

public static class RootResolver
{
  public static string Resolve(string? explicitRoot, string[] args)
  {
    if (!string.IsNullOrWhiteSpace(explicitRoot))
      return Path.GetFullPath(explicitRoot);

    for (var i = 0; i < args.Length - 1; i++)
    {
      if (args[i] is "--root" or "-r")
        return Path.GetFullPath(args[i + 1]);
    }

    var env = Environment.GetEnvironmentVariable("SMARTGUARD_ROOT");
    if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);

    return ResolveFromBaseDirectory(AppContext.BaseDirectory, explicitRoot, args);
  }

  public static string ResolveFromBaseDirectory(string baseDirectory, string? explicitRoot, string[] args)
  {
    if (!string.IsNullOrWhiteSpace(explicitRoot))
      return Path.GetFullPath(explicitRoot);

    for (var i = 0; i < args.Length - 1; i++)
    {
      if (args[i] is "--root" or "-r")
        return Path.GetFullPath(args[i + 1]);
    }

    var env = Environment.GetEnvironmentVariable("SMARTGUARD_ROOT");
    if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);

    var dir = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    if (string.Equals(Path.GetFileName(dir), "bin", StringComparison.OrdinalIgnoreCase))
    {
      var installRoot = Directory.GetParent(dir)?.FullName;
      if (!string.IsNullOrWhiteSpace(installRoot))
        return installRoot;
    }

    dir = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    for (var depth = 0; depth < 6; depth++)
    {
      if (File.Exists(Path.Combine(dir, "SmartGuard.config.json")))
        return dir;
      var parent = Directory.GetParent(dir);
      if (parent is null) break;
      dir = parent.FullName;
    }

    throw new InvalidOperationException(
      "无法确定 SmartGuard 安装目录。请使用 --root 指定，或设置 SMARTGUARD_ROOT 环境变量。");
  }
}
