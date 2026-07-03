namespace SmartGuard.Configuration;

public static class InstallRootResolver
{
  public static string Resolve(string? explicitRoot, string[] args)
    => ResolveFromBaseDirectory(AppContext.BaseDirectory, explicitRoot, args);

  internal static string NormalizeRootPath(string root)
  {
    var normalized = Path.GetFullPath(root.Trim().Trim('"'));
    var pathRoot = Path.GetPathRoot(normalized);
    if (!string.IsNullOrWhiteSpace(pathRoot)
        && normalized.Length > pathRoot.Length)
    {
      normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    return normalized;
  }

  public static string ResolveFromBaseDirectory(string baseDirectory, string? explicitRoot, string[] args)
  {
    if (!string.IsNullOrWhiteSpace(explicitRoot))
      return NormalizeRootPath(explicitRoot);

    for (var i = 0; i < args.Length - 1; i++)
    {
      if (args[i] is "--root" or "-r")
        return NormalizeRootPath(args[i + 1]);
    }

    var env = Environment.GetEnvironmentVariable("SMARTGUARD_ROOT");
    if (!string.IsNullOrWhiteSpace(env))
      return NormalizeRootPath(env);

    var dir = Path.GetFullPath(baseDirectory)
      .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    if (string.Equals(Path.GetFileName(dir), "bin", StringComparison.OrdinalIgnoreCase))
    {
      var installRoot = Directory.GetParent(dir)?.FullName;
      if (!string.IsNullOrWhiteSpace(installRoot))
        return installRoot;
    }

    dir = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    for (var depth = 0; depth < 6; depth++)
    {
      if (File.Exists(Path.Combine(dir, SmartGuardPaths.ConfigFileName)))
        return dir;
      var parent = Directory.GetParent(dir);
      if (parent is null) break;
      dir = parent.FullName;
    }

    throw new InvalidOperationException(
      "无法确定 SmartGuard 安装目录。请使用 --root 指定，或设置 SMARTGUARD_ROOT 环境变量。");
  }
}
