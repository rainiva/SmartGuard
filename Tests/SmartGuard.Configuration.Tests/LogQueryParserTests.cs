using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogQueryParserTests
{
  [Fact]
  public void Parse_explicit_AND_expression()
  {
    var result = LogQueryParser.Parse("severity:ERROR AND component:Engine");

    result.IsValid.Should().BeTrue();
    result.Expression.Should().BeOfType<LogQueryAndExpression>();
  }

  [Fact]
  public void Parse_explicit_OR_expression()
  {
    var result = LogQueryParser.Parse("severity:ERROR OR severity:WARN");

    result.IsValid.Should().BeTrue();
    result.Expression.Should().BeOfType<LogQueryOrExpression>();
  }

  [Fact]
  public void Parse_implicit_AND_between_terms()
  {
    var result = LogQueryParser.Parse("severity:ERROR component:Engine");

    result.IsValid.Should().BeTrue();
    result.Expression.Should().BeOfType<LogQueryAndExpression>();
  }

  [Fact]
  public void Parse_parentheses_grouping()
  {
    var result = LogQueryParser.Parse("(severity:ERROR OR severity:WARN) AND component:Engine");

    result.IsValid.Should().BeTrue();
    var root = result.Expression.Should().BeOfType<LogQueryAndExpression>().Subject;
    root.Left.Should().BeOfType<LogQueryOrExpression>();
    root.Right.Should().BeOfType<LogQueryFieldExpression>();
  }

  [Fact]
  public void Parse_NOT_prefix()
  {
    var result = LogQueryParser.Parse("NOT severity:INFO");

    result.IsValid.Should().BeTrue();
    result.Expression.Should().BeOfType<LogQueryNotExpression>();
  }

  [Fact]
  public void Parse_dash_negation_prefix()
  {
    var result = LogQueryParser.Parse("-severity:INFO");

    result.IsValid.Should().BeTrue();
    result.Expression.Should().BeOfType<LogQueryNotExpression>();
  }

  [Theory]
  [InlineData(":")]
  [InlineData("=")]
  [InlineData("!=")]
  [InlineData(">")]
  [InlineData(">=")]
  [InlineData("<")]
  [InlineData("<=")]
  [InlineData("~")]
  public void Parse_field_operators(string op)
  {
    var text = op switch
    {
      ":" => "severity:ERROR",
      "=" => "severity=ERROR",
      "!=" => "severity!=INFO",
      ">" => "time>2026-08-01T00:00:00Z",
      ">=" => "time>=2026-08-01T00:00:00Z",
      "<" => "time<2026-08-02T00:00:00Z",
      "<=" => "time<=2026-08-02T00:00:00Z",
      "~" => "message~timeout",
      _ => throw new InvalidOperationException(),
    };

    var result = LogQueryParser.Parse(text);

    result.IsValid.Should().BeTrue();
    var field = result.Expression.Should().BeOfType<LogQueryFieldExpression>().Subject;
    field.Operator.Should().Be(LogQueryOperatorTokens.FromToken(op));
  }

  [Fact]
  public void Parse_quoted_string_with_escape()
  {
    var result = LogQueryParser.Parse("message:\"hello \\\"world\\\"\"");

    result.IsValid.Should().BeTrue();
    var field = result.Expression.Should().BeOfType<LogQueryFieldExpression>().Subject;
    field.Value.Should().Be("hello \"world\"");
  }

  [Fact]
  public void Parse_level_alias_maps_to_severity()
  {
    var result = LogQueryParser.Parse("level:ERROR");

    result.IsValid.Should().BeTrue();
    var field = result.Expression.Should().BeOfType<LogQueryFieldExpression>().Subject;
    field.Field.Should().Be("severity");
    field.Value.Should().Be("ERROR");
  }

  [Theory]
  [InlineData("severity")]
  [InlineData("component")]
  [InlineData("channel")]
  [InlineData("event")]
  [InlineData("op")]
  [InlineData("trace")]
  [InlineData("ua")]
  [InlineData("pid")]
  [InlineData("tid")]
  [InlineData("message")]
  [InlineData("time")]
  public void Parse_supported_fields(string fieldName)
  {
    var text = fieldName switch
    {
      "pid" => "pid:1234",
      "tid" => "tid:5678",
      "time" => "time>=2026-08-01T00:00:00Z",
      _ => $"{fieldName}:value",
    };

    var result = LogQueryParser.Parse(text);

    result.IsValid.Should().BeTrue();
    result.Expression.Should().BeOfType<LogQueryFieldExpression>();
  }

  [Fact]
  public void Parse_bare_word_becomes_message_contains()
  {
    var result = LogQueryParser.Parse("timeout");

    result.IsValid.Should().BeTrue();
    var field = result.Expression.Should().BeOfType<LogQueryFieldExpression>().Subject;
    field.Field.Should().Be("message");
    field.Operator.Should().Be(LogQueryOperator.Contains);
    field.Value.Should().Be("timeout");
  }

  [Fact]
  public void Parse_invalid_field_returns_position()
  {
    var result = LogQueryParser.Parse("bogus:value");

    result.IsValid.Should().BeFalse();
    result.ErrorPosition.Should().Be(0);
    result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    result.Expression.Should().BeNull();
  }

  [Fact]
  public void Parse_unbalanced_parenthesis_returns_position()
  {
    var result = LogQueryParser.Parse("(severity:ERROR");

    result.IsValid.Should().BeFalse();
    result.ErrorPosition.Should().NotBeNull();
    result.Expression.Should().BeNull();
  }
}
