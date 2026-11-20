using System.IO.Compression;
using System.Text;

namespace SmartGuard.Configuration;

public static partial class DiagnosticBundleExporter
{
  private static void AddJsonFileEntry(string sourcePath, string entryPath, int maxBytes,
    Func<string, string> redact, ZipArchive archive, List<ManifestFileEntry> entries, CancellationToken token)
  {
    var status = "unreadable";
    long? sourceBytes = null;
    string? sanitized = null;
    try
    {
      using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      sourceBytes = stream.Length;
      if (sourceBytes > maxBytes) status = "too-large";
      else
      {
        var bytes = new byte[(int)sourceBytes];
        var received = 0;
        while (received < bytes.Length)
        {
          token.ThrowIfCancellationRequested();
          var read = stream.Read(bytes, received, Math.Min(8192, bytes.Length - received));
          if (read == 0) throw new IOException("Diagnostic source changed during read.");
          received += read;
        }
        status = "redaction-failed";
        sanitized = redact(new UTF8Encoding(false, true).GetString(bytes));
        status = "included";
      }
    }
    catch (OperationCanceledException) { throw; }
    catch (FileNotFoundException) { status = "missing"; }
    catch (DirectoryNotFoundException) { status = "missing"; }
    catch { /* Fixed metadata only; never emit a raw source or exception path. */ }
    var metadata = new ManifestFileEntry(entryPath, status, false, null)
    { ReadStatus = status, SourceBytes = sourceBytes, OmittedReason = sanitized is null ? status : null };
    if (sanitized is null) entries.Add(metadata);
    else WriteTextEntry(archive, entryPath, sanitized, entries, metadata);
  }

  private static void AddOptionalRedactedFileEntry(string sourcePath, string entryPath,
    ZipArchive archive, List<ManifestFileEntry> entries, CancellationToken token)
  {
    var result = DiagnosticLogTailReader.ReadTail(sourcePath, DiagnosticLogTailReader.MaxBytes, token);
    var lines = new Queue<(string Text, int Bytes)>();
    var bytes = 0;
    var failed = false;
    var truncated = result.Truncated;
    foreach (var line in (result.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
      token.ThrowIfCancellationRequested();
      try
      {
        string redacted;
        if (line.TrimStart().StartsWith('{'))
        {
          if (!LogJsonlFormatter.TryParseLine(line, out var item)) { failed = true; continue; }
          redacted = LogJsonlFormatter.FormatLine(DiagnosticRedactor.RedactEvent(item!));
        }
        else redacted = DiagnosticRedactor.RedactText(line);
        var length = Encoding.UTF8.GetByteCount(redacted) + 1;
        if (length > DiagnosticLogTailReader.MaxBytes) { truncated = true; continue; }
        lines.Enqueue((redacted, length)); bytes += length;
        while (bytes > DiagnosticLogTailReader.MaxBytes) { bytes -= lines.Dequeue().Bytes; truncated = true; }
      }
      catch { failed = true; }
    }
    var status = lines.Count == 0 && failed ? "redaction-failed" : ReadStatusCode(result.Status);
    // Maintenance is independent bounded writer work. Reader byte evidence
    // describes only the 260KiB tail read, not a legacy 1MiB repair read.
    var maintenance = SmartGuardLogWriter.MaintainSupplementalLog(sourcePath);
    var metadata = new ManifestFileEntry(entryPath, lines.Count > 0 && (truncated || failed) ? "truncated" : status, truncated || failed, null)
    { ReadStatus = status, SourceBytes = result.SourceBytes, OmittedReason = failed ? "redaction-failed" : truncated ? "byte-budget-or-partial-line" : null,
      MaintenanceCompleted = maintenance.Completed };
    if (lines.Count == 0 && (result.Status != DiagnosticReadStatus.Included || failed || truncated)) entries.Add(metadata);
    else if (lines.Count == 0) WriteTextEntry(archive, entryPath, "", entries, metadata);
    else WriteTextEntry(archive, entryPath, string.Join("\n", lines.Select(item => item.Text)) + "\n", entries, metadata);
  }

  private static string ReadStatusCode(DiagnosticReadStatus status) => status switch
  {
    DiagnosticReadStatus.Included => "included", DiagnosticReadStatus.Missing => "missing", DiagnosticReadStatus.TooLarge => "too-large",
    DiagnosticReadStatus.RedactionFailed => "redaction-failed", _ => "unreadable",
  };

  private static void PublishBundle(string tempPath, string destination, bool overwrite)
  {
    if (!File.Exists(destination)) { File.Move(tempPath, destination); return; }
    if (!overwrite) throw new IOException("Diagnostic destination appeared without overwrite permission.");
    var backup = tempPath + ".recovery.bak";
    try { File.Replace(tempPath, destination, backup); }
    catch
    {
      // No delete-and-move fallback. If replacement displaced the destination,
      // restore its same-directory backup; a failed restore leaves it intact.
      if (File.Exists(backup) && !File.Exists(destination))
      {
        try { File.Move(backup, destination); }
        catch { throw new IOException("Diagnostic publication failed; recovery backup retained: " + backup); }
      }
      throw;
    }
    TryDeleteFile(backup);
  }
}
