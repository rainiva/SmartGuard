using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartGuard.Configuration;

public static class DiagnosticBundleExporter
{
  public const int MainLogTailMaxBytes = 512 * 1024;

  private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

  public static void Export(string root, string destinationZipPath, string? appVersion = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(root);
    ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);

    var destinationDirectory = Path.GetDirectoryName(destinationZipPath);
    if (!string.IsNullOrEmpty(destinationDirectory))
      Directory.CreateDirectory(destinationDirectory);

    if (File.Exists(destinationZipPath))
      File.Delete(destinationZipPath);

    var fileEntries = new List<ManifestFileEntry>();
    using (var stream = new FileStream(destinationZipPath, FileMode.CreateNew, FileAccess.Write))
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
    {
      AddConfigEntry(root, archive, fileEntries);
      AddStatusEntry(root, archive, fileEntries);
      AddMainLogEntry(root, archive, fileEntries);
      AddStartupLogEntry(root, archive, fileEntries);
      AddJsonlLogEntry(root, archive, fileEntries);

      var manifestJson = BuildManifestJson(appVersion, fileEntries);
      WriteTextEntry(archive, "manifest.json", manifestJson);
    }
  }

  internal static string SanitizeConfigJson(string configJson)
  {
    var node = JsonNode.Parse(configJson);
    if (node is not JsonObject obj)
      return configJson;

    obj.Remove(GitHubTokenProtector.LegacyFieldName);
    obj.Remove(GitHubTokenProtector.ProtectedFieldName);
    return obj.ToJsonString(JsonWriteOptions);
  }

  private static void AddConfigEntry(string root, ZipArchive archive, List<ManifestFileEntry> fileEntries)
  {
    const string entryPath = "config/SmartGuard.config.json";
    var sourcePath = SmartGuardPaths.ConfigFile(root);
    if (!File.Exists(sourcePath))
    {
      fileEntries.Add(ManifestFileEntry.Missing(entryPath));
      return;
    }

    var sanitized = SanitizeConfigJson(File.ReadAllText(sourcePath));
    WriteTextEntry(archive, entryPath, sanitized);
    fileEntries.Add(ManifestFileEntry.Included(entryPath));
  }

  private static void AddStatusEntry(string root, ZipArchive archive, List<ManifestFileEntry> fileEntries)
  {
    const string entryPath = "status/SmartGuard.status.json";
    var sourcePath = SmartGuardPaths.StatusFile(root);
    if (!File.Exists(sourcePath))
    {
      fileEntries.Add(ManifestFileEntry.Missing(entryPath));
      return;
    }

    WriteBinaryEntry(archive, entryPath, File.ReadAllBytes(sourcePath));
    fileEntries.Add(ManifestFileEntry.Included(entryPath));
  }

  private static void AddMainLogEntry(string root, ZipArchive archive, List<ManifestFileEntry> fileEntries)
  {
    const string entryPath = "logs/SmartGuard.log";
    var config = new GuardConfigRepository(SmartGuardPaths.ConfigFile(root)).TryLoad();
    var sourcePath = config is null
      ? SmartGuardPaths.DefaultLogFile(root)
      : SmartGuardPaths.ResolveLogFile(config, root);

    if (!File.Exists(sourcePath))
    {
      fileEntries.Add(ManifestFileEntry.Missing(entryPath));
      return;
    }

    var fileInfo = new FileInfo(sourcePath);
    if (fileInfo.Length <= MainLogTailMaxBytes)
    {
      WriteBinaryEntry(archive, entryPath, File.ReadAllBytes(sourcePath));
      fileEntries.Add(ManifestFileEntry.Included(entryPath));
      return;
    }

    var tailBytes = ReadTailBytes(sourcePath, MainLogTailMaxBytes);
    WriteBinaryEntry(archive, entryPath, tailBytes);
    fileEntries.Add(ManifestFileEntry.TruncatedEntry(entryPath));
  }

  private static void AddStartupLogEntry(string root, ZipArchive archive, List<ManifestFileEntry> fileEntries)
  {
    const string entryPath = "logs/SmartGuard.startup.log";
    var sourcePath = SmartGuardPaths.StartupLogFile(root);
    if (!File.Exists(sourcePath))
    {
      fileEntries.Add(ManifestFileEntry.Missing(entryPath));
      return;
    }

    WriteBinaryEntry(archive, entryPath, File.ReadAllBytes(sourcePath));
    fileEntries.Add(ManifestFileEntry.Included(entryPath));
  }

  private static void AddJsonlLogEntry(string root, ZipArchive archive, List<ManifestFileEntry> fileEntries)
  {
    const string entryPath = "logs/SmartGuard.jsonl";
    var config = new GuardConfigRepository(SmartGuardPaths.ConfigFile(root)).TryLoad();
    var sourcePath = config is null
      ? SmartGuardPaths.DefaultLogFile(root)
      : SmartGuardPaths.ResolveLogFile(config, root);

    if (!File.Exists(sourcePath))
    {
      fileEntries.Add(ManifestFileEntry.Missing(entryPath));
      return;
    }

    var tailBytes = ReadTailBytes(sourcePath, MainLogTailMaxBytes);
    var tailText = Encoding.UTF8.GetString(tailBytes);
    var rawLines = tailText.Split('\n');
    var source = LogSourceIdentity.FromFilePath(sourcePath);
    var startIndex = new FileInfo(sourcePath).Length > MainLogTailMaxBytes ? 1 : 0;
    var jsonlLines = new List<string>();
    long byteOffset = 0;
    for (var i = startIndex; i < rawLines.Length; i++)
    {
      var line = rawLines[i].TrimEnd('\r');
      if (string.IsNullOrWhiteSpace(line))
      {
        byteOffset += Encoding.UTF8.GetByteCount(rawLines[i]) + 1;
        continue;
      }

      if (LogJsonlFormatter.TryParseEvent(line, out var structured) && structured is not null)
      {
        jsonlLines.Add(LogJsonlFormatter.FormatLine(structured));
      }
      else
      {
        var decoded = LogEventDecoder.Decode(new LogPhysicalLine(source, byteOffset, line, true));
        jsonlLines.Add(LogJsonlFormatter.FormatLine(decoded));
      }

      byteOffset += Encoding.UTF8.GetByteCount(rawLines[i]) + 1;
    }

    if (jsonlLines.Count == 0)
    {
      fileEntries.Add(ManifestFileEntry.Missing(entryPath));
      return;
    }

    WriteTextEntry(archive, entryPath, string.Join("\n", jsonlLines));
    fileEntries.Add(ManifestFileEntry.Included(entryPath));
  }

  private static byte[] ReadTailBytes(string sourcePath, int maxBytes)
  {
    using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var length = stream.Length;
    var bytesToRead = (int)Math.Min(maxBytes, length);
    stream.Seek(-bytesToRead, SeekOrigin.End);
    var buffer = new byte[bytesToRead];
    var read = stream.Read(buffer, 0, bytesToRead);
    if (read == bytesToRead)
      return buffer;

    var trimmed = new byte[read];
    Array.Copy(buffer, trimmed, read);
    return trimmed;
  }

  private static string BuildManifestJson(string? appVersion, IReadOnlyList<ManifestFileEntry> files)
  {
    var manifest = new JsonObject
    {
      ["exportedAt"] = DateTimeOffset.Now.ToString("o"),
      ["appVersion"] = appVersion ?? ResolveAppVersion(),
      ["osVersion"] = RuntimeInformation.OSDescription,
      ["files"] = new JsonArray(files.Select(f => f.ToJson()).ToArray()),
    };
    return manifest.ToJsonString(JsonWriteOptions);
  }

  private static string ResolveAppVersion()
  {
    var version = typeof(DiagnosticBundleExporter).Assembly.GetName().Version;
    if (version is null)
      return "unknown";

    return version.Build > 0
      ? $"{version.Major}.{version.Minor}.{version.Build}"
      : $"{version.Major}.{version.Minor}";
  }

  private static void WriteTextEntry(ZipArchive archive, string entryPath, string content)
    => WriteBinaryEntry(archive, entryPath, Encoding.UTF8.GetBytes(content));

  private static void WriteBinaryEntry(ZipArchive archive, string entryPath, byte[] content)
  {
    var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
    using var entryStream = entry.Open();
    entryStream.Write(content, 0, content.Length);
  }

  private sealed record ManifestFileEntry(string Path, string Status, bool Truncated)
  {
    internal static ManifestFileEntry Included(string path) => new(path, "included", false);
    internal static ManifestFileEntry Missing(string path) => new(path, "missing", false);
    internal static ManifestFileEntry TruncatedEntry(string path) => new(path, "truncated", true);

    internal JsonObject ToJson()
    {
      var node = new JsonObject
      {
        ["path"] = Path,
        ["status"] = Status,
      };
      if (Truncated)
        node["truncated"] = true;
      return node;
    }
  }
}
