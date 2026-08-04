using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class DiagnosticRedactorTests
{
  [Fact]
  public void RedactText_redacts_user_profile_paths()
  {
    var input = @"C:\Users\john.doe\Documents\SmartGuard\logs\app.log";

    var redacted = DiagnosticRedactor.RedactText(input);

    redacted.Should().NotContain("john.doe");
    redacted.Should().Contain($@"\Users\{DiagnosticRedactor.RedactedUserSegment}\");
  }

  [Fact]
  public void RedactText_redacts_sensitive_command_arguments()
  {
    var input = "smartguard.exe --token=ghp_superSecretToken123 --password hunter2";

    var redacted = DiagnosticRedactor.RedactText(input);

    redacted.Should().NotContain("ghp_superSecretToken123");
    redacted.Should().NotContain("hunter2");
    redacted.Should().Contain(DiagnosticRedactor.RedactedSecretValue);
  }

  [Fact]
  public void RedactConfigJson_removes_github_tokens_and_sensitive_fields()
  {
    const string token = "ghp_secretExportToken1234567890";
    var protectedValue = GitHubTokenProtector.Protect(token)!;
    var json = $$"""
      {
        "BalancedThresholdSec": 300,
        "GitHubToken": "{{token}}",
        "GitHubTokenProtected": "{{protectedValue}}",
        "secret": "should-be-redacted"
      }
      """;

    var sanitized = DiagnosticRedactor.RedactConfigJson(json);

    sanitized.Should().NotContain("GitHubToken");
    sanitized.Should().NotContain("GitHubTokenProtected");
    sanitized.Should().NotContain(token);
    sanitized.Should().Contain("BalancedThresholdSec");
    sanitized.Should().Contain(DiagnosticRedactor.RedactedSecretValue);
  }

  [Fact]
  public void RedactConfigJson_redacts_saved_filter_names_and_query_values()
  {
    var json = """
      {
        "BalancedThresholdSec": 300,
        "LogViewPreferences": {
          "DefaultFilterId": "secret-filter",
          "SavedFilters": [
            {
              "Id": "secret-filter",
              "Name": "C:\\Users\\alice\\敏感关键词",
              "Query": {
                "SchemaVersion": 1,
                "Kind": "field",
                "Field": "message",
                "Operator": "Contains",
                "Value": "C:\\Users\\alice\\token=super-secret"
              }
            }
          ]
        }
      }
      """;

    var sanitized = DiagnosticRedactor.RedactConfigJson(json);

    sanitized.Should().NotContain("alice");
    sanitized.Should().NotContain("敏感关键词");
    sanitized.Should().NotContain("super-secret");
    sanitized.Should().Contain("LogViewPreferences");
    sanitized.Should().Contain(DiagnosticRedactor.RedactedSecretValue);
  }

  [Fact]
  public void RedactEvent_redacts_message_and_property_secrets_without_mutating_original()
  {
    var properties = new Dictionary<string, System.Text.Json.JsonElement>
    {
      ["password"] = System.Text.Json.JsonSerializer.SerializeToElement("plain-password"),
    };
    var original = new LogEvent(
      LogEvent.CurrentSchemaVersion,
      "event-1",
      DateTimeOffset.UtcNow,
      LogSeverity.Info,
      "Engine",
      "Lifecycle",
      "AppStarted",
      "Loaded config from C:\\Users\\alice\\AppData\\token=abc123",
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      properties);

    var redacted = DiagnosticRedactor.RedactEvent(original);

    redacted.Message.Should().NotContain("alice");
    redacted.Message.Should().NotContain("abc123");
    redacted.Properties["password"].GetString().Should().Be(DiagnosticRedactor.RedactedSecretValue);
    original.Message.Should().Contain("alice");
    original.Properties["password"].GetString().Should().Be("plain-password");
  }
}
