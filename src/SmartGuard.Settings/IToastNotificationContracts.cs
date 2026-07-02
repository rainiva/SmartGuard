using System.Windows;

namespace SmartGuard.Settings;

public interface IToastWindow
{
  void Show();
  void Close();
  event EventHandler? Closed;
}

public interface IToastWindowFactory
{
  IToastWindow Create(string message, bool isError, bool isDarkMode, Window owner);
}
