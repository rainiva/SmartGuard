using System.Text;

namespace SmartGuard.Configuration;

public static class ExceptionFormatter
{
  public static string Format(Exception ex)
  {
    if (ex is null) return string.Empty;
    var sb = new StringBuilder();
    AppendException(sb, ex, 0);
    return sb.ToString();
  }

  private static void AppendException(StringBuilder sb, Exception ex, int depth)
  {
    var indent = new string(' ', depth * 2);
    sb.Append(indent).Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
    if (!string.IsNullOrEmpty(ex.StackTrace))
      sb.Append(Environment.NewLine).Append(indent)
        .Append("StackTrace: ")
        .Append(ex.StackTrace!.Replace(Environment.NewLine, " | "));

    if (ex is AggregateException aggregate)
    {
      foreach (var inner in aggregate.Flatten().InnerExceptions)
      {
        sb.Append(Environment.NewLine).Append(indent).Append("---> AggregateInner:").Append(Environment.NewLine);
        AppendException(sb, inner, depth + 1);
      }
      return;
    }

    if (ex.InnerException is not null)
    {
      sb.Append(Environment.NewLine).Append(indent).Append("---> InnerException:").Append(Environment.NewLine);
      AppendException(sb, ex.InnerException, depth + 1);
    }
  }
}
