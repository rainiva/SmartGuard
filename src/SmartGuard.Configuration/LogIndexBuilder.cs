using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartGuard.Configuration;

/// <summary>
/// 日志索引构建器：按行解析日志文件构建内存索引。
/// 支持 CancellationToken 取消，解析失败的单行跳过不计入索引。
/// </summary>
public sealed class LogIndexBuilder
{
  // 匹配格式: [LEVEL] yyyy-MM-dd HH:mm:ss [Category] EventName op=xxx
  private static readonly Regex LinePattern = new(
    @"^\[(\w+)\]\s+(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+\[?(\w+)\]?\s*(.*)$",
    RegexOptions.Compiled);

  private static readonly Regex OpPattern = new(@"op=(\S+)", RegexOptions.Compiled);
  private static readonly Regex TracePattern = new(@"trace=(\S+)", RegexOptions.Compiled);

  public LogIndex Build(string logPath, CancellationToken cancellationToken)
  {
    if (!File.Exists(logPath))
      return LogIndex.Empty(logPath);

    var entries = new List<LogIndexEntry>();
    var lineNumber = 0;

    using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);

    string? line;
    while ((line = reader.ReadLine()) != null)
    {
      if (cancellationToken.IsCancellationRequested)
        break;

      lineNumber++;
      var entry = TryParseLine(line, lineNumber);
      if (entry != null)
        entries.Add(entry);
    }

    return new LogIndex(entries, logPath);
  }

  internal static LogIndexEntry? TryParseLine(string line, int lineNumber)
  {
    var match = LinePattern.Match(line);
    if (!match.Success)
      return null;

    var level = match.Groups[1].Value;
    var timestampStr = match.Groups[2].Value;
    var category = match.Groups[3].Value;
    var remainder = match.Groups[4].Value;

    if (!DateTimeOffset.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss",
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp))
      return null;

    // 解析 OperationId
    string? operationId = null;
    var opMatch = OpPattern.Match(remainder);
    if (opMatch.Success)
      operationId = opMatch.Groups[1].Value;

    // 解析 EventName（remainder 中第一个单词，空格前）
    var eventName = remainder.Split(' ')[0];
    if (string.IsNullOrEmpty(eventName) || eventName == "op=" || eventName.Contains('='))
      eventName = null;

    // 解析 TraceId
    string? traceId = null;
    var traceMatch = TracePattern.Match(remainder);
    if (traceMatch.Success)
      traceId = traceMatch.Groups[1].Value;

    return new LogIndexEntry(
      lineNumber, timestamp, level, category, eventName, operationId, traceId, null, null);
  }
}
