using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

/// <summary>
/// 设置专属候选发现：优先有窗口的进程，再读本机开始菜单；已确认WPS的目录仅检查四个文档组件。
/// 不启动程序、不解析启动参数、不将候选发现作为名单授权。
/// 每窗口最多一个在途调用，合作取消/两秒预算、2048个对象、512个候选；平台单次调用不可强制中断。
/// 发现不完全时可继续浏览文件，不能以候选目录替代 Engine 的真实前台识别。
/// </summary>
internal static class SettingsProgramCatalog
{
  internal const int MaxCandidates = 512;
  internal const int MaxResults = 20;
  private const int MaxInspected = 2048;

  internal static SettingsProgramCandidate? ReadForegroundCandidate()
  {
    var observation = new SmartGuard.Engine.Infrastructure.ForegroundProgramReader().Read();
    if (!observation.IdentificationSucceeded || observation.ExecutablePath is not string path
      || path.StartsWith(@"\\", StringComparison.Ordinal)
      || string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return null;
    var name = Path.GetFileNameWithoutExtension(path);
    try
    {
      var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
      if (!string.IsNullOrWhiteSpace(description)) name = description;
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    return Normalize([new SettingsProgramCandidate(name, path, ReadIcon(path), ProgramCandidateSource.RunningWindow)]).FirstOrDefault();
  }

  internal static IReadOnlyList<SettingsProgramCandidate> Normalize(IEnumerable<SettingsProgramCandidate> candidates)
  {
    var unique = new Dictionary<string, SettingsProgramCandidate>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in candidates.Take(MaxInspected))
    {
      if (!GuardConfigValidator.TryNormalizeProgramPath(item.ExecutablePath, out var path)) continue;
      var name = string.IsNullOrWhiteSpace(item.Name) ? Path.GetFileNameWithoutExtension(path) : item.Name.Trim();
      // Start menus include maintenance shortcuts; do not suggest an uninstaller as an app.
      if (name.StartsWith("卸载", StringComparison.Ordinal)
        || Path.GetFileName(path).StartsWith("unins", StringComparison.OrdinalIgnoreCase)) continue;
      var executable = Path.GetFileName(path);
      // The start-menu launcher does not own document windows; listing it creates an unusable rule.
      if (executable.Equals("ksolaunch.exe", StringComparison.OrdinalIgnoreCase)) continue;
      if (name.StartsWith("WPS", StringComparison.OrdinalIgnoreCase))
      {
        name = executable.ToLowerInvariant() switch
        {
          "wps.exe" => "WPS 文字",
          "et.exe" => "WPS 表格",
          "wpp.exe" => "WPS 演示",
          "wpspdf.exe" => "WPS PDF",
          _ => name,
        };
      }
      if (!unique.TryGetValue(path, out var previous) ||
        (item.Source == ProgramCandidateSource.RunningWindow && previous.Source != ProgramCandidateSource.RunningWindow))
        unique[path] = new SettingsProgramCandidate(name[..Math.Min(name.Length, 256)], path, item.Icon, item.Source);
      if (unique.Count == MaxCandidates) break;
    }
    return unique.Values.ToArray();
  }

  internal static IReadOnlyList<SettingsProgramCandidate> Search(IReadOnlyList<SettingsProgramCandidate> candidates, string query)
  {
    query = query.Trim();
    if (query.Length == 0) return [];
    query = query[..Math.Min(query.Length, 128)];
    return candidates.Take(MaxCandidates)
      .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(item.ExecutablePath).Contains(query, StringComparison.OrdinalIgnoreCase))
      .OrderBy(item => item.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
        : item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
      .Take(MaxResults).ToArray();
  }

  internal static Task<IReadOnlyList<SettingsProgramCandidate>> LoadAsync(CancellationToken cancellation)
    => LoadAsync(cancellation, [Environment.GetFolderPath(Environment.SpecialFolder.Programs),
      Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)]);

  internal static Task<IReadOnlyList<SettingsProgramCandidate>> LoadAsync(CancellationToken cancellation,
    IReadOnlyList<string> shortcutRoots)
  {
    // Shell automation owns COM objects on this one STA worker; none cross into the UI.
    var completion = new TaskCompletionSource<IReadOnlyList<SettingsProgramCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var worker = new Thread(() =>
    {
      try { completion.TrySetResult(Discover(cancellation, shortcutRoots)); }
      catch (OperationCanceledException) { completion.TrySetCanceled(cancellation); }
      catch (Exception error) { completion.TrySetException(error); }
    }) { IsBackground = true, Name = "SmartGuard.ProgramCatalog" };
    worker.SetApartmentState(ApartmentState.STA);
    worker.Start();
    return completion.Task;
  }

  private static IReadOnlyList<SettingsProgramCandidate> Discover(CancellationToken cancellation, IReadOnlyList<string> shortcutRoots)
  {
    var elapsed = Stopwatch.StartNew();
    var candidates = new List<SettingsProgramCandidate>();
    var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var inspected = 0;
    bool WithinBudget()
    {
      cancellation.ThrowIfCancellationRequested();
      return elapsed.Elapsed < TimeSpan.FromSeconds(2) && inspected < MaxInspected && candidates.Count < MaxCandidates;
    }
    void Add(string name, string? path, ProgramCandidateSource source)
    {
      if (!GuardConfigValidator.TryNormalizeProgramPath(path, out var normalized)
        || normalized.StartsWith(@"\\", StringComparison.Ordinal)) return;
      if (seen.TryGetValue(normalized, out var index))
      {
        // WPS sibling suggestions may precede that component's own process in enumeration order.
        if (source == ProgramCandidateSource.RunningWindow)
          candidates[index] = new SettingsProgramCandidate(candidates[index].Name, normalized, candidates[index].Icon, source);
        return;
      }
      if (!File.Exists(normalized)) return;
      seen.Add(normalized, candidates.Count);
      candidates.Add(new SettingsProgramCandidate(name, normalized, ReadIcon(normalized), source));
    }

    // Window owners take priority: shortcut discovery can consume the entire bounded budget.
    var processes = Process.GetProcesses();
    var componentFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    try
    {
      foreach (var process in processes)
      {
        if (!WithinBudget()) break;
        inspected++;
        try
        {
          if (process.MainWindowHandle == IntPtr.Zero) continue;
          var path = process.MainModule?.FileName;
          if (path is null) continue;
          var name = FileVersionInfo.GetVersionInfo(path).FileDescription;
          Add(string.IsNullOrWhiteSpace(name) ? process.ProcessName : name, path, ProgramCandidateSource.RunningWindow);
          if (name?.StartsWith("WPS", StringComparison.OrdinalIgnoreCase) != true) continue;
          var folder = Path.GetDirectoryName(path);
          if (folder is null || folder.StartsWith(@"\\", StringComparison.Ordinal) || !componentFolders.Add(folder)) continue;
          // Inspect only the four document components next to a verified WPS window owner.
          // Discovery adds suggestions, never config entries or broad directory matching.
          foreach (var component in new[] { "wps.exe", "et.exe", "wpp.exe", "wpspdf.exe" })
          {
            if (!WithinBudget()) break;
            inspected++;
            var componentPath = Path.Combine(folder, component);
            if (!File.Exists(componentPath)) continue;
            var description = FileVersionInfo.GetVersionInfo(componentPath).FileDescription;
            if (description?.StartsWith("WPS", StringComparison.OrdinalIgnoreCase) == true) Add(description, componentPath, ProgramCandidateSource.RelatedComponent);
          }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException
          or IOException or UnauthorizedAccessException or NotSupportedException) { }
      }
    }
    finally { foreach (var process in processes) process.Dispose(); }
    if (!WithinBudget()) return Normalize(candidates);

    object? shell = null;
    try
    {
      var shellType = Type.GetTypeFromProgID("WScript.Shell");
      if (shellType is not null) shell = Activator.CreateInstance(shellType);
      foreach (var folder in shortcutRoots.Take(2))
      {
        foreach (var linkPath in EnumerateLinks(folder, WithinBudget))
        {
          if (!WithinBudget()) break;
          inspected++;
          object? link = null;
          try
          {
            if (shell is null) break;
            link = ((dynamic)shell).CreateShortcut(linkPath);
            string target = ((dynamic)link).TargetPath;
            // Do not Resolve: link tracking may search disks/network or prompt for missing targets.
            Add(Path.GetFileNameWithoutExtension(linkPath), target, ProgramCandidateSource.Shortcut);
          }
          catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException) { }
          finally { if (link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link); }
        }
      }
    }
    catch (COMException) { /* Shell automation may be disabled; running programs remain discoverable. */ }
    finally { if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }

    return Normalize(candidates);
  }

  private static IEnumerable<string> EnumerateLinks(string root, Func<bool> withinBudget)
  {
    var folders = new Stack<(string Path, int Depth)>();
    folders.Push((root, 0));
    var visited = 0;
    while (folders.Count > 0 && visited++ < MaxInspected && withinBudget())
    {
      var (folder, depth) = folders.Pop();
      string[] entries;
      try
      {
        // Skip junctions to avoid cycles and targets outside the selected local start-menu roots.
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
        entries = Directory.EnumerateFileSystemEntries(folder).Take(MaxInspected).ToArray();
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
      foreach (var entry in entries)
      {
        if (!withinBudget()) yield break;
        if (entry.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) yield return entry;
        else if (depth < 8 && folders.Count < MaxInspected && Directory.Exists(entry)) folders.Push((entry, depth + 1));
      }
    }
  }

  private static ImageSource? ReadIcon(string path)
  {
    try
    {
      using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
      if (icon is null) return null;
      var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
        System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(24, 24));
      image.Freeze();
      return image;
    }
    catch (Exception error) when (error is ArgumentException or IOException or System.ComponentModel.Win32Exception
      or UnauthorizedAccessException or ExternalException) { return null; }
  }
}
