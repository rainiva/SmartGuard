using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class GuardConfigSavedLogFilterTests
{
  [Fact]
  public void Save_persists_saved_filters_with_chinese_names_and_default()
  {
    var dir = CreateTempDir();
    var path = ConfigPath(dir);

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      var filterId = "filter-errors";
      config.LogViewPreferences.SavedFilters.Add(new SavedLogFilter
      {
        Id = filterId,
        Name = "错误与警告",
        Query = LogQueryExpressionDto.FromExpression(
          new LogQueryFieldExpression("severity", LogQueryOperator.Equals, "Error")),
      });
      config.LogViewPreferences.DefaultFilterId = filterId;
      repo.Save(config);

      var reloaded = repo.LoadOrDefault(dir);
      reloaded.LogViewPreferences.DefaultFilterId.Should().Be(filterId);
      reloaded.LogViewPreferences.SavedFilters.Should().ContainSingle();
      reloaded.LogViewPreferences.SavedFilters[0].Name.Should().Be("错误与警告");
      File.ReadAllText(path).Should().Contain("错误与警告");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Save_round_trips_versioned_query_ast()
  {
    var dir = CreateTempDir();
    var path = ConfigPath(dir);

    try
    {
      var expression = LogQueryExpression.AndAll([
        new LogQueryFieldExpression("component", LogQueryOperator.Equals, "Engine"),
        new LogQueryOrExpression(
          new LogQueryFieldExpression("severity", LogQueryOperator.Equals, "Error"),
          new LogQueryNotExpression(
            new LogQueryFieldExpression("message", LogQueryOperator.Contains, "heartbeat"))),
      ]);

      var repo = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      config.LogViewPreferences.SavedFilters.Add(new SavedLogFilter
      {
        Id = "complex",
        Name = "Complex",
        Query = LogQueryExpressionDto.FromExpression(expression),
      });
      repo.Save(config);

      var reloaded = repo.LoadOrDefault(dir);
      var roundTripped = reloaded.LogViewPreferences.SavedFilters[0].Query.ToExpression();
      LogQueryExpressionComparer.AreEquivalent(expression, roundTripped).Should().BeTrue();
      reloaded.LogViewPreferences.SavedFilters[0].Query.SchemaVersion
        .Should().Be(LogQueryExpressionDto.CurrentSchemaVersion);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void TryLoad_defaults_log_view_preferences_when_fields_missing()
  {
    var dir = CreateTempDir();
    var path = ConfigPath(dir);
    File.WriteAllText(path, """
      {
        "BalancedThresholdSec": 300
      }
      """);

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = repo.LoadOrDefault(dir);

      config.LogViewPreferences.Should().NotBeNull();
      config.LogViewPreferences.SavedFilters.Should().BeEmpty();
      config.LogViewPreferences.DefaultFilterId.Should().BeNull();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Save_preserves_unknown_future_fields_when_updating_saved_filters()
  {
    var dir = CreateTempDir();
    var path = ConfigPath(dir);
    File.WriteAllText(path, """
      {
        "BalancedThresholdSec": 300,
        "FutureWorkbenchFlag": true
      }
      """);

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = repo.LoadOrDefault(dir);
      config.LogViewPreferences.SavedFilters.Add(new SavedLogFilter
      {
        Id = "one",
        Name = "One",
        Query = LogQueryExpressionDto.FromExpression(LogQueryMatchAllExpression.Instance),
      });
      repo.Save(config);

      File.ReadAllText(path).Should().Contain("FutureWorkbenchFlag");
      File.ReadAllText(path).Should().Contain("\"Name\": \"One\"");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Save_persists_filter_order_and_default_id()
  {
    var dir = CreateTempDir();
    var path = ConfigPath(dir);

    try
    {
      var repo = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      config.LogViewPreferences.SavedFilters.AddRange([
        new SavedLogFilter { Id = "a", Name = "Alpha", Query = LogQueryExpressionDto.MatchAll() },
        new SavedLogFilter { Id = "b", Name = "Beta", Query = LogQueryExpressionDto.MatchAll() },
        new SavedLogFilter { Id = "c", Name = "Gamma", Query = LogQueryExpressionDto.MatchAll() },
      ]);
      config.LogViewPreferences.DefaultFilterId = "b";
      repo.Save(config);

      var reloaded = repo.LoadOrDefault(dir);
      reloaded.LogViewPreferences.SavedFilters.Select(f => f.Id).Should().Equal("a", "b", "c");
      reloaded.LogViewPreferences.DefaultFilterId.Should().Be("b");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  private static string CreateTempDir()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }

  private static string ConfigPath(string dir) => Path.Combine(dir, "SmartGuard.config.json");
}
