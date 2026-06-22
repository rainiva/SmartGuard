using FluentAssertions;
using SmartGuard.Tray.Toast;

namespace SmartGuard.Tray.Tests;

public class ToastAumidRegistrarTests
{
    [Fact]
    public void EnsureRegistered_skips_app_user_model_id_on_second_call()
    {
        var root = Path.Combine(Path.GetTempPath(), "SmartGuardToastRegAumid_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));

        try
        {
            ToastAumidRegistrar.ResetForTests();
            ToastAumidRegistrar.StartMenuShortcutWriterForTests = _ => true;

            ToastAumidRegistrar.EnsureRegistered(root);
            ToastAumidRegistrar.EnsureRegistered(root);

            ToastAumidRegistrar.AppUserModelIdWriteCountForTests.Should().Be(1);
        }
        finally
        {
            ToastAumidRegistrar.ResetForTests();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void EnsureRegistered_skips_start_menu_shortcut_on_second_call()
    {
        var root = Path.Combine(Path.GetTempPath(), "SmartGuardToastReg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));

        try
        {
            ToastAumidRegistrar.ResetForTests();
            ToastAumidRegistrar.StartMenuShortcutWriterForTests = _ => true;

            ToastAumidRegistrar.EnsureRegistered(root);
            ToastAumidRegistrar.EnsureRegistered(root);

            ToastAumidRegistrar.ShortcutWriteCountForTests.Should().Be(1);
        }
        finally
        {
            ToastAumidRegistrar.ResetForTests();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
