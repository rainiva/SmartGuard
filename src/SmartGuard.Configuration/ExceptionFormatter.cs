using System.Text;

namespace SmartGuard.Configuration;

public static class ExceptionFormatter
{
  private const int MaxDepth = 32;

  public static string Format(Exception ex)
  {
    if (ex is null) return string.Empty;
    var sb = new StringBuilder();
    var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
    AppendException(sb, ex, 0, seen);
    return sb.ToString();
  }

  private static void AppendException(StringBuilder sb, Exception ex, int depth, HashSet<Exception> seen)
  {
    var indent = new string(' ', depth * 2);
    if (!seen.Add(ex))
    {
      sb.Append(indent).Append("[循环异常链已截断]");
      return;
    }

    sb.Append(indent).Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
    if (!string.IsNullOrEmpty(ex.StackTrace))
      sb.Append(Environment.NewLine).Append(indent)
        .Append("StackTrace: ")
        .Append(ex.StackTrace!.Replace(Environment.NewLine, " | "));

    if (depth >= MaxDepth)
    {
      sb.Append(Environment.NewLine).Append(indent)
        .Append($"[异常链深度超过 {MaxDepth}，已截断]");
      return;
    }

    if (ex is AggregateException aggregate)
    {
      foreach (var inner in aggregate.Flatten().InnerExceptions)
      {
        sb.Append(Environment.NewLine).Append(indent).Append("---> AggregateInner:").Append(Environment.NewLine);
        AppendException(sb, inner, depth + 1, seen);
      }
      return;
    }

    if (ex.InnerException is not null)
    {
      sb.Append(Environment.NewLine).Append(indent).Append("---> InnerException:").Append(Environment.NewLine);
      AppendException(sb, ex.InnerException, depth + 1, seen);
    }
  }
}
