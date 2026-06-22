using System.Windows;
using System.Windows.Threading;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class NumberBoxTests
{
    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        WpfStaTestHost.Run(() => result = action());
        return result!;
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);

    [Fact]
    public void Initial_value_matches_default()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox();
            box.Value.Should().Be(0);
        });
    }

    [Fact]
    public void Increment_button_increases_value()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 5, Minimum = 0, Maximum = 100 };
            box.IncrementCommand.Execute(null);
            box.Value.Should().Be(6);
        });
    }

    [Fact]
    public void Decrement_button_decreases_value()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 5, Minimum = 0, Maximum = 100 };
            box.DecrementCommand.Execute(null);
            box.Value.Should().Be(4);
        });
    }

    [Fact]
    public void Value_does_not_exceed_maximum()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 100, Minimum = 0, Maximum = 100 };
            box.IncrementCommand.Execute(null);
            box.Value.Should().Be(100);
        });
    }

    [Fact]
    public void Value_does_not_go_below_minimum()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 0, Minimum = 0, Maximum = 100 };
            box.DecrementCommand.Execute(null);
            box.Value.Should().Be(0);
        });
    }

    [Fact]
    public void ValueChanged_event_fires_on_increment()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 5, Minimum = 0, Maximum = 100 };
            var fired = false;
            box.ValueChanged += (_, _) => fired = true;

            box.IncrementCommand.Execute(null);

            fired.Should().BeTrue();
        });
    }

    [Fact]
    public void ValueChanged_event_fires_on_decrement()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 5, Minimum = 0, Maximum = 100 };
            var fired = false;
            box.ValueChanged += (_, _) => fired = true;

            box.DecrementCommand.Execute(null);

            fired.Should().BeTrue();
        });
    }

    [Fact]
    public void Value_changed_directly_fires_event()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 5, Minimum = 0, Maximum = 100 };
            var fired = false;
            box.ValueChanged += (_, _) => fired = true;

            box.Value = 10;

            fired.Should().BeTrue();
        });
    }

    [Fact]
    public void Value_respects_minimum_maximum_when_set_directly()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 50, Minimum = 10, Maximum = 90 };

            box.Value = 5;
            box.Value.Should().Be(10);

            box.Value = 100;
            box.Value.Should().Be(90);
        });
    }

    [Fact]
    public void SmallChange_default_is_one()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox();
            box.SmallChange.Should().Be(1);
        });
    }

    [Fact]
    public void SmallChange_custom_value_works()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 10, Minimum = 0, Maximum = 100, SmallChange = 5 };
            box.IncrementCommand.Execute(null);
            box.Value.Should().Be(15);
        });
    }

    [Fact]
    public void TickFrequency_maps_to_small_change()
    {
        RunOnSta(() =>
        {
            var box = new NumberBox { Value = 0, Minimum = 0, Maximum = 100 };
            box.TickFrequency = 10;
            box.SmallChange.Should().Be(10);
        });
    }
}
