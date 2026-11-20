using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

public sealed class LogTablePresenterTests
{
  [Fact]
  public void ApplyPage_replaces_current_page_and_exposes_only_current_rows()
  {
    var presenter = new LogTablePresenter();
    var firstPage = new[] { CreateEvent("first"), CreateEvent("second") };
    var secondPage = new[] { CreateEvent("third") };

    presenter.ApplyPage(firstPage, pageSize: 20);
    presenter.Rows.Select(row => row.SourceEvent.Message).Should().Equal("first", "second");

    presenter.ApplyPage(secondPage, pageSize: 20);
    presenter.Rows.Select(row => row.SourceEvent.Message).Should().Equal("third");
    presenter.Rows.Should().HaveCount(1);
    presenter.PageSize.Should().Be(20);
  }

  [Fact]
  public void ApplyPage_rejects_more_than_one_hundred_rows()
  {
    var presenter = new LogTablePresenter();
    var events = Enumerable.Range(0, 101).Select(index => CreateEvent($"row-{index}"));

    Action act = () => presenter.ApplyPage(events.ToArray(), pageSize: 100);

    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void ApplyPage_rejects_non_positive_page_size()
  {
    var presenter = new LogTablePresenter();

    Action act = () => presenter.ApplyPage([CreateEvent("row")], pageSize: 0);

    act.Should().Throw<ArgumentOutOfRangeException>();
  }

  [Fact]
  public void ApplyPage_empty_page_clears_previous_rows()
  {
    var presenter = new LogTablePresenter();
    presenter.ApplyPage([CreateEvent("row")], pageSize: 20);

    presenter.ApplyPage(Array.Empty<LogEvent>(), pageSize: 20);

    presenter.Rows.Should().BeEmpty();
  }

  [Fact]
  public void Presenter_does_not_keep_a_second_full_history_event_collection()
  {
    typeof(LogTablePresenter)
      .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
      .Should()
      .NotContain(field => field.FieldType == typeof(List<LogEvent>));
  }

  private static LogEvent CreateEvent(string message)
    => new(
      LogEvent.CurrentSchemaVersion,
      Guid.NewGuid().ToString("N"),
      new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero),
      LogSeverity.Info,
      "Engine",
      "Runtime",
      "TestEvent",
      message,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      LogEvent.EmptyProperties);
}
