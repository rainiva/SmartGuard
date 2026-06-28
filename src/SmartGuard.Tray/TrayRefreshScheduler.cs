using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Tray.Toast;

namespace SmartGuard.Tray;

internal sealed class TrayRefreshUiUpdate
{
  public required StatusPayload? Status { get; init; }

  public required TrayNotificationDecision? Notification { get; init; }

  public required TrayNotificationDedupeState DedupeState { get; init; }

  public required bool StatusWasMissing { get; init; }
}

internal sealed class TrayRefreshScheduler
{
  private readonly string _root;
  private readonly StatusStore _statusStore;
  private readonly TrayDisplaySettingsCache _displaySettingsCache;
  private readonly TrayNotificationPresenter _notificationPresenter;
  private readonly Control _invokeSink;
  private readonly Action<TrayRefreshUiUpdate> _applyUi;

  private bool _refreshInFlight;
  private bool _refreshDeferred;
  private bool _contextMenuOpen;
  private TrayNotificationDedupeState _dedupeState = TrayNotificationDedupeState.Empty;

  internal bool RefreshDeferredForTests => _refreshDeferred;

  internal int RefreshScheduleCountForTests { get; private set; }

  public TrayRefreshScheduler(
    string root,
    StatusStore statusStore,
    TrayDisplaySettingsCache displaySettingsCache,
    TrayNotificationPresenter notificationPresenter,
    Control invokeSink,
    Action<TrayRefreshUiUpdate> applyUi)
  {
    _root = root;
    _statusStore = statusStore;
    _displaySettingsCache = displaySettingsCache;
    _notificationPresenter = notificationPresenter;
    _invokeSink = invokeSink;
    _applyUi = applyUi;
  }

  public bool ContextMenuOpen
  {
    get => _contextMenuOpen;
    set => _contextMenuOpen = value;
  }

  public void ScheduleRefresh()
  {
    RefreshScheduleCountForTests++;
    if (TrayMenuRefreshGuard.ShouldDefer(_contextMenuOpen))
    {
      _refreshDeferred = true;
      return;
    }

    if (_refreshInFlight)
    {
      _refreshDeferred = true;
      return;
    }

    _refreshInFlight = true;
    _ = Task.Run(RefreshCore);
  }

  public void OnMenuClosed()
  {
    _contextMenuOpen = false;
    if (!_refreshDeferred)
      return;

    _refreshDeferred = false;
    ScheduleRefresh();
  }

  private void RefreshCore()
  {
    try
    {
      var status = _statusStore.Read();
      var prefs = new TrayNotificationPreferences(
        _displaySettingsCache.NotifyOnPlanChange,
        _displaySettingsCache.NotifyOnExternalChange);
      var (decision, nextDedupe) = TrayNotificationEvaluator.Evaluate(status, prefs, _dedupeState);

      TrayNotificationDecision? uiDecision = decision;
      if (decision is { UseBalloonFallback: false } structured)
      {
        var channel = _notificationPresenter.Show(
          structured.Title,
          structured.Body,
          structured.Tag,
          (_, _) => { });
        if (channel != TrayNotificationChannel.Toast)
          uiDecision = structured with { UseBalloonFallback = true };
      }

      var update = new TrayRefreshUiUpdate
      {
        Status = status,
        Notification = uiDecision,
        DedupeState = nextDedupe,
        StatusWasMissing = status is null,
      };

      if (_invokeSink.IsHandleCreated)
        _invokeSink.BeginInvoke(() => ApplyUi(update));
      else
        ApplyUi(update);
    }
    finally
    {
      _refreshInFlight = false;
      if (_refreshDeferred && !_contextMenuOpen)
      {
        _refreshDeferred = false;
        ScheduleRefresh();
      }
    }
  }

  private void ApplyUi(TrayRefreshUiUpdate update)
  {
    _dedupeState = update.DedupeState;
    _applyUi(update);
  }
}
