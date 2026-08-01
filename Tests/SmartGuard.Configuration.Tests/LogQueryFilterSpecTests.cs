using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogQueryFilterSpecTests
{
  [Fact]
  public void Default_spec_has_no_filters_and_default_pagination()
  {
    var spec = LogFilterBuilder.Create().Build();

    spec.Keyword.Should().BeNull();
    spec.Level.Should().BeNull();
    spec.Page.Should().Be(1);
    spec.PageSize.Should().Be(50);
  }

  [Fact]
  public void Spec_is_immutable_record()
  {
    var spec = new LogQueryFilterSpec(null, "ERROR", null, null, null, null, null, null, null, null, 1, 50);
    var modified = spec with { Level = "WARN" };

    spec.Level.Should().Be("ERROR");
    modified.Level.Should().Be("WARN");
  }

  [Fact]
  public void Spec_with_all_fields_set_round_trips()
  {
    var spec = new LogQueryFilterSpec(
      "crash", "ERROR", "Engine", "op-abc", "trace-xyz",
      1234, 5678, "IterationError",
      new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
      new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero),
      2, 100);

    spec.Keyword.Should().Be("crash");
    spec.Page.Should().Be(2);
    spec.PageSize.Should().Be(100);
  }

  [Fact]
  public void Spec_builder_conversion()
  {
    var spec = LogFilterBuilder.Create()
      .WithKeyword("error")
      .WithLevel("ERROR")
      .WithOperationId("op-abc")
      .Build();

    spec.Keyword.Should().Be("error");
    spec.Level.Should().Be("ERROR");
    spec.OperationId.Should().Be("op-abc");
  }

  [Fact]
  public void ToQueryRequest_maps_all_non_time_fields()
  {
    var spec = new LogQueryFilterSpec(
      "crash", "ERROR", "Engine", "op-abc", "trace-xyz",
      1234, 5678, "IterationError",
      new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
      new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero), 2, 100);
    var request = spec.ToQueryRequest();
    request.Keyword.Should().Be("crash");
    request.Level.Should().Be("ERROR");
    request.Category.Should().Be("Engine");
    request.OperationId.Should().Be("op-abc");
    request.TraceId.Should().Be("trace-xyz");
    request.ProcessId.Should().Be(1234);
    request.ThreadId.Should().Be(5678);
    request.EventName.Should().Be("IterationError");
    request.Page.Should().Be(2);
    request.PageSize.Should().Be(100);
  }
}
