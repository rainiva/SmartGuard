using System.Windows;

namespace SmartGuard.Settings.Tests;

public class ToastNotificationServiceTests
{
    private static T RunOnSta<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task.Result;
    }

    private static void RunOnSta(Action action)
    {
        RunOnSta(() => { action(); return true; });
    }

    private sealed class FakeToast : IToastWindow
    {
        public bool IsShown { get; private set; }
        public bool IsClosed { get; private set; }
        public int ShowCount { get; private set; }

        public void Show()
        {
            IsShown = true;
            ShowCount++;
        }

        public void Close()
        {
            IsClosed = true;
        }

        public event EventHandler? Closed;

        public void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ServiceHarness
    {
        public List<FakeToast> Toasts { get; } = new();
        public ToastNotificationService Service { get; }

        public ServiceHarness()
        {
            var window = new Window();
            Service = new ToastNotificationService(
                window,
                TimeSpan.FromSeconds(3),
                (message, isError, owner) =>
                {
                    var toast = new FakeToast();
                    Toasts.Add(toast);
                    return toast;
                });
        }
    }

    [Fact]
    public void Show_same_message_twice_does_not_create_second_toast()
    {
        RunOnSta(() =>
        {
            var harness = new ServiceHarness();

            harness.Service.Show("设置已保存", isError: false);
            harness.Service.Show("设置已保存", isError: false);

            harness.Toasts.Should().ContainSingle();
            harness.Toasts[0].ShowCount.Should().Be(1);
        });
    }

    [Fact]
    public void Show_different_message_closes_previous_toast_and_creates_new_one()
    {
        RunOnSta(() =>
        {
            var harness = new ServiceHarness();

            harness.Service.Show("设置已保存", isError: false);
            harness.Service.Show("保存失败：无效值", isError: true);

            harness.Toasts.Should().HaveCount(2);
            harness.Toasts[0].IsClosed.Should().BeTrue();
            harness.Toasts[1].IsShown.Should().BeTrue();
        });
    }

    [Fact]
    public void Show_after_toast_closed_creates_new_toast()
    {
        RunOnSta(() =>
        {
            var harness = new ServiceHarness();

            harness.Service.Show("设置已保存", isError: false);
            harness.Toasts[0].RaiseClosed();
            harness.Service.Show("设置已保存", isError: false);

            harness.Toasts.Should().HaveCount(2);
            harness.Toasts[1].IsShown.Should().BeTrue();
        });
    }
}
