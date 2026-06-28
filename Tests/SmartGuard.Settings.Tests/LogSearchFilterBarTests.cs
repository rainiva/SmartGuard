namespace SmartGuard.Settings.Tests;

using System.Windows;
using System.Windows.Controls;

[Collection("WpfUiTests")]
public class LogSearchFilterBarTests
{
    [Fact]
    public void AddTagFilter_creates_chip_with_matching_palette_color()
    {
        RunOnSta(() =>
        {
            var bar = new LogSearchFilterBar();
            bar.AddTagFilter("INFO");

            bar.ActiveTags.Should().ContainSingle("INFO");
            bar.Children.Count.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void RemoveTagFilter_clears_chip_from_search_area()
    {
        RunOnSta(() =>
        {
            var bar = new LogSearchFilterBar();
            bar.AddTagFilter("WARN");
            bar.RemoveTagFilter("WARN");

            bar.ActiveTags.Should().BeEmpty();
        });
    }

    [Fact]
    public void AddTagFilter_is_idempotent_for_same_tag()
    {
        RunOnSta(() =>
        {
            var bar = new LogSearchFilterBar();
            bar.AddTagFilter("ERROR");
            bar.AddTagFilter("ERROR");

            bar.ActiveTags.Should().ContainSingle("ERROR");
        });
    }

    [Fact]
    public void Chip_remove_button_template_includes_hover_and_pressed_triggers()
    {
        RunOnSta(() =>
        {
            var bar = new LogSearchFilterBar();
            bar.AddTagFilter("INFO");

            var removeButton = GetChipRemoveButton(bar);
            removeButton.ApplyTemplate();

            removeButton.Template!.FindName("bd", removeButton).Should().BeOfType<Border>(
                "chip remove button should use interactive template");
            var triggers = removeButton.Template.Triggers.OfType<Trigger>().ToList();
            triggers.Should().Contain(t => t.Property == UIElement.IsMouseOverProperty);
            triggers.Should().Contain(t => t.Property == Button.IsPressedProperty);
        });
    }

    private static Button GetChipRemoveButton(LogSearchFilterBar bar)
    {
        var chipPanel = bar.Children.OfType<WrapPanel>().Single();
        var chip = chipPanel.Children.OfType<Border>().Single();
        var content = (StackPanel)chip.Child!;
        return content.Children.OfType<Button>().Single();
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);
}
