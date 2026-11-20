using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace SmartGuard.Settings;

internal static class LogStatusLiveRegion
{
  internal static void Set(TextBlock statusLabel, string text)
  {
    statusLabel.Text = text;

    var peer = FrameworkElementAutomationPeer.FromElement(statusLabel)
      ?? FrameworkElementAutomationPeer.CreatePeerForElement(statusLabel);
    peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
  }
}
