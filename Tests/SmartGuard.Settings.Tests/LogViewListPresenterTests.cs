using System.Windows.Controls;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class LogViewListPresenterTests
{
    [Fact]
    public void Apply_replace_all_populates_virtualized_list_items()
    {
        RunOnSta(() =>
        {
            var listBox = CreateListBox();
            var presenter = new LogViewListPresenter();
            presenter.Attach(listBox);

            presenter.Apply(LogViewUpdatePlan.ReplaceAll(
                ["[INFO] 2026-06-21 10:00:00 alpha", "[WARN] 2026-06-21 10:01:00 beta"]));

            presenter.GetPlainText().Should().Contain("alpha");
            presenter.GetPlainText().Should().Contain("beta");
            listBox.Items.Count.Should().Be(2);
        });
    }

    [Fact]
    public void Apply_append_tail_adds_items_without_rebuilding_existing_rows()
    {
        RunOnSta(() =>
        {
            var listBox = CreateListBox();
            var presenter = new LogViewListPresenter();
            presenter.Attach(listBox);

            presenter.Apply(LogViewUpdatePlan.ReplaceAll(["[INFO] line 1"]));
            var initialItem = listBox.Items[0];

            presenter.Apply(LogViewUpdatePlan.AppendTail(
                ["[INFO] line 2"],
                ["[INFO] line 1", "[INFO] line 2"]));

            listBox.Items.Count.Should().Be(2);
            ReferenceEquals(listBox.Items[0], initialItem).Should().BeTrue();
            presenter.GetPlainText().Should().Be("[INFO] line 1" + Environment.NewLine + "[INFO] line 2");
        });
    }

    [Fact]
    public void Apply_no_change_keeps_items_and_returns_unchanged()
    {
        RunOnSta(() =>
        {
            var presenter = new LogViewListPresenter();
            presenter.Attach(CreateListBox());

            presenter.Apply(LogViewUpdatePlan.ReplaceAll(["[INFO] line 1"]));
            var result = presenter.Apply(LogViewUpdatePlan.NoChange(["[INFO] line 1"]));

            result.Should().Be(LogViewApplyResult.Unchanged);
            presenter.GetPlainText().Should().Be("[INFO] line 1");
        });
    }

    [Fact]
    public void ListBox_is_configured_for_virtualization()
    {
        RunOnSta(() =>
        {
            var listBox = CreateListBox();
            VirtualizingPanel.GetIsVirtualizing(listBox).Should().BeTrue();
            VirtualizingPanel.GetVirtualizationMode(listBox).Should().Be(VirtualizationMode.Recycling);
            ScrollViewer.GetCanContentScroll(listBox).Should().BeTrue();
        });
    }

    private static ListBox CreateListBox()
    {
        var listBox = new ListBox();
        LogViewListPresenter.ConfigureListBox(listBox);
        return listBox;
    }

    private static void RunOnSta(Action action) => WpfStaTestHost.Run(action);
}
