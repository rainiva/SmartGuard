using System.Globalization;
using System.Text;

namespace SmartGuard.Configuration;

public static class LogQueryParser
{
  private static readonly HashSet<string> SupportedFields = new(StringComparer.OrdinalIgnoreCase)
  {
    "severity", "level", "component", "category", "channel", "event", "op", "trace", "ua", "pid", "tid", "message", "time",
  };

  public static LogQueryValidationResult Parse(string? text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return LogQueryValidationResult.Failure(0, "Query expression is empty.");

    try
    {
      var parser = new Parser(text);
      var expression = parser.ParseExpression();
      parser.ExpectEnd();
      return LogQueryValidationResult.Success(expression);
    }
    catch (ParseException ex)
    {
      return LogQueryValidationResult.Failure(ex.Position, ex.Message);
    }
  }

  internal static string NormalizeField(string field) => field.ToLowerInvariant() switch
  {
    "level" => "severity",
    "category" => "component",
    "event" => "eventname",
    "op" => "operationid",
    "trace" => "traceid",
    "ua" => "useractionid",
    "pid" => "processid",
    "tid" => "threadid",
    _ => field.ToLowerInvariant(),
  };

  private sealed class ParseException : Exception
  {
    public int Position { get; }

    public ParseException(int position, string message)
      : base(message)
    {
      Position = position;
    }
  }

  private sealed class Parser
  {
    private readonly string _text;
    private int _index;

    public Parser(string text) => _text = text;

    public LogQueryExpression ParseExpression() => ParseOr();

    public void ExpectEnd()
    {
      SkipWhitespace();
      if (_index < _text.Length)
        throw new ParseException(_index, "Unexpected trailing input.");
    }

    private LogQueryExpression ParseOr()
    {
      var left = ParseAnd();
      while (TryReadKeyword("OR"))
      {
        var right = ParseAnd();
        left = new LogQueryOrExpression(left, right);
      }

      return left;
    }

    private LogQueryExpression ParseAnd()
    {
      var parts = new List<LogQueryExpression> { ParseUnary() };
      while (true)
      {
        SkipWhitespace();
        if (_index >= _text.Length)
          break;

        if (PeekKeyword("OR"))
          break;

        if (PeekKeyword("AND") || CanStartUnary())
        {
          if (PeekKeyword("AND"))
            TryReadKeyword("AND");

          parts.Add(ParseUnary());
          continue;
        }

        break;
      }

      return LogQueryExpression.AndAll(parts);
    }

    private LogQueryExpression ParseUnary()
    {
      SkipWhitespace();
      if (TryReadKeyword("NOT") || TryReadChar('-'))
        return new LogQueryNotExpression(ParseUnary());

      return ParsePrimary();
    }

    private LogQueryExpression ParsePrimary()
    {
      SkipWhitespace();
      if (TryReadChar('('))
      {
        var expr = ParseExpression();
        if (!TryReadChar(')'))
          throw new ParseException(_index, "Expected closing parenthesis.");

        return expr;
      }

      return ParseAtom();
    }

    private LogQueryExpression ParseAtom()
    {
      SkipWhitespace();
      var start = _index;

      if (TryReadQuotedString(out var quoted))
        return new LogQueryFieldExpression("message", LogQueryOperator.Contains, quoted);

      var identifier = ReadIdentifier();
      if (identifier.Length == 0)
        throw new ParseException(_index, "Expected field, keyword, or quoted string.");

      SkipWhitespace();
      if (TryReadOperator(out var op))
      {
        var field = NormalizeField(identifier);
        if (!SupportedFields.Contains(identifier) && !SupportedFields.Contains(field))
          throw new ParseException(start, $"Unknown field '{identifier}'.");

        var value = op == LogQueryOperator.Regex ? ReadRegexValue() : ReadValue();
        return new LogQueryFieldExpression(field, op, value);
      }

      return new LogQueryFieldExpression("message", LogQueryOperator.Contains, identifier);
    }

    private string ReadValue()
    {
      SkipWhitespace();
      if (TryReadQuotedString(out var quoted))
        return quoted;

      return ReadBareValue();
    }

    private string ReadRegexValue()
    {
      SkipWhitespace();
      if (TryReadQuotedString(out var quoted))
        return quoted;

      return ReadBalancedBareValue();
    }

    private string ReadBalancedBareValue()
    {
      SkipWhitespace();
      var start = _index;
      var depth = 0;
      while (_index < _text.Length)
      {
        var ch = _text[_index];
        if (ch == '(')
        {
          depth++;
          _index++;
          continue;
        }

        if (ch == ')')
        {
          if (depth == 0)
            break;

          depth--;
          _index++;
          continue;
        }

        if (depth == 0)
        {
          if (char.IsWhiteSpace(ch))
            break;

          if (IsKeywordAt(_index, "AND") || IsKeywordAt(_index, "OR") || IsKeywordAt(_index, "NOT"))
            break;
        }

        _index++;
      }

      if (_index == start)
        throw new ParseException(_index, "Expected value after operator.");

      return _text[start.._index];
    }

    private string ReadBareValue()
    {
      SkipWhitespace();
      var start = _index;
      while (_index < _text.Length)
      {
        if (char.IsWhiteSpace(_text[_index]))
          break;

        if (_text[_index] == ')')
          break;

        if (IsKeywordAt(_index, "AND") || IsKeywordAt(_index, "OR") || IsKeywordAt(_index, "NOT"))
          break;

        _index++;
      }

      if (_index == start)
        throw new ParseException(_index, "Expected value after operator.");

      return _text[start.._index];
    }

    private bool TryReadOperator(out LogQueryOperator op)
    {
      SkipWhitespace();
      foreach (var token in new[] { "!=", ">=", "<=", ":", "=", ">", "<", "~" })
      {
        if (MatchesAt(_index, token))
        {
          _index += token.Length;
          op = LogQueryOperatorTokens.FromToken(token);
          return true;
        }
      }

      op = default;
      return false;
    }

    private bool TryReadQuotedString(out string value)
    {
      SkipWhitespace();
      if (_index >= _text.Length || _text[_index] != '"')
      {
        value = string.Empty;
        return false;
      }

      _index++;
      var builder = new StringBuilder();
      while (_index < _text.Length)
      {
        var ch = _text[_index++];
        if (ch == '"')
        {
          value = builder.ToString();
          return true;
        }

        if (ch == '\\')
        {
          if (_index >= _text.Length)
            throw new ParseException(_index, "Unexpected end of quoted string.");

          builder.Append(_text[_index++]);
          continue;
        }

        builder.Append(ch);
      }

      throw new ParseException(_index, "Unterminated quoted string.");
    }

    private string ReadIdentifier()
    {
      SkipWhitespace();
      var start = _index;
      while (_index < _text.Length && IsIdentifierChar(_text[_index]))
        _index++;

      return _text[start.._index];
    }

    private bool CanStartUnary()
    {
      SkipWhitespace();
      if (_index >= _text.Length)
        return false;

      return _text[_index] == '('
             || _text[_index] == '-'
             || _text[_index] == '"'
             || IsIdentifierChar(_text[_index]);
    }

    private bool TryReadKeyword(string keyword)
    {
      SkipWhitespace();
      if (!IsKeywordAt(_index, keyword))
        return false;

      _index += keyword.Length;
      if (_index < _text.Length && IsIdentifierChar(_text[_index]))
        return false;

      return true;
    }

    private bool PeekKeyword(string keyword)
    {
      SkipWhitespace();
      return IsKeywordAt(_index, keyword);
    }

    private bool IsKeywordAt(int index, string keyword)
    {
      if (!MatchesAt(index, keyword))
        return false;

      if (index > 0 && IsIdentifierChar(_text[index - 1]))
        return false;

      var next = index + keyword.Length;
      return next >= _text.Length || !IsIdentifierChar(_text[next]);
    }

    private bool TryReadChar(char expected)
    {
      SkipWhitespace();
      if (_index >= _text.Length || _text[_index] != expected)
        return false;

      _index++;
      return true;
    }

    private void SkipWhitespace()
    {
      while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
        _index++;
    }

    private bool MatchesAt(int index, string value)
      => index + value.Length <= _text.Length
         && string.Compare(_text, index, value, 0, value.Length, StringComparison.Ordinal) == 0;

    private static bool IsIdentifierChar(char ch)
      => char.IsLetterOrDigit(ch) || ch is '_' or '.';
  }
}
