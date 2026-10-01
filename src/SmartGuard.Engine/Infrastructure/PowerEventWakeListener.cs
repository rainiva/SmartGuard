using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SmartGuard.Engine.Infrastructure;

internal sealed class PowerEventWakeListener : IDisposable
{
  private readonly Action<PowerLifecycleSignal> _onPowerChanged;
  private readonly TimeProvider _timeProvider;
  private bool _subscribed;
  private bool _usedTestOverride;
  private readonly object _gate = new();
  private IDisposable? _nativeSubscription;
  private bool _disposed;
  private bool _resumeDelivered;
  private volatile bool _enabled;
  private volatile string? _failureReason;

  internal static Action<PowerModeChangedEventHandler>? SubscribeOverrideForTests;
  internal static Action<PowerModeChangedEventHandler>? UnsubscribeOverrideForTests;
  internal static Func<Action<uint>, IDisposable>? NativeRegisterOverrideForTests;

  public bool IsEnabled => _enabled;
  public string? FailureReason => _failureReason;

  private PowerEventWakeListener(
    Action<PowerLifecycleSignal> onPowerChanged,
    TimeProvider timeProvider,
    bool subscribe)
  {
    _onPowerChanged = onPowerChanged;
    _timeProvider = timeProvider;
    if (!subscribe)
      return;

    try
    {
      _enabled = true;
      Subscribe();
    }
    catch (Exception ex) when (ex is PlatformNotSupportedException or Win32Exception or DllNotFoundException or EntryPointNotFoundException)
    {
      _failureReason = ex.Message;
      Dispose();
    }
  }

  internal PowerEventWakeListener(
    Action<PowerLifecycleSignal> onPowerChanged,
    TimeProvider? timeProvider = null)
    : this(onPowerChanged, timeProvider ?? TimeProvider.System, subscribe: true)
  {
  }

  internal static PowerEventWakeListener Create(
    Action<PowerLifecycleSignal> onPowerChanged,
    TimeProvider? timeProvider = null)
    => new(onPowerChanged, timeProvider ?? TimeProvider.System, subscribe: true);

  private void Subscribe()
  {
    if (SubscribeOverrideForTests is not null)
    {
      SubscribeOverrideForTests(OnPowerModeChanged);
      _usedTestOverride = true;
    }
    else
    {
      SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    _subscribed = true;
    _nativeSubscription = NativeRegisterOverrideForTests is { } register
      ? register(OnNativePowerChanged)
      : new NativeRegistration(OnNativePowerChanged);
  }

  private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
  {
    if (e.Mode == PowerModes.StatusChange)
      Publish(PowerLifecycleKind.StatusChanged);
  }

  private void OnNativePowerChanged(uint type)
  {
    lock (_gate)
    {
      if (_disposed || _failureReason is not null) return;
      if (type == 4) // PBT_APMSUSPEND
      {
        _resumeDelivered = false;
        Publish(PowerLifecycleKind.Suspending);
      }
      else if (type is 18 or 7 && !_resumeDelivered)
      {
        _resumeDelivered = true;
        Publish(PowerLifecycleKind.Resumed);
      }
    }
  }

  private void Publish(PowerLifecycleKind kind)
  {
    lock (_gate)
    {
      if (_disposed || _failureReason is not null) return;
      try { _onPowerChanged(new PowerLifecycleSignal(kind, _timeProvider.GetUtcNow())); }
      catch (Exception ex)
      {
        _failureReason = ex.Message;
        _enabled = false;
      }
    }
  }

  public void Dispose()
  {
    lock (_gate)
    {
      if (_disposed) return;
      _disposed = true;
      _enabled = false;
    }

    try
    {
      if (_subscribed)
      {
        if (_usedTestOverride) UnsubscribeOverrideForTests?.Invoke(OnPowerModeChanged);
        else SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _subscribed = false;
      }
    }
    finally { _nativeSubscription?.Dispose(); }
  }

  private sealed class NativeRegistration : IDisposable
  {
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint Callback(IntPtr context, uint type, IntPtr setting);
    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters { public IntPtr Callback; public IntPtr Context; }
    [DllImport("Powrprof.dll")]
    private static extern uint PowerRegisterSuspendResumeNotification(uint flags, ref Parameters recipient, out IntPtr handle);
    [DllImport("Powrprof.dll")]
    private static extern uint PowerUnregisterSuspendResumeNotification(IntPtr handle);

    private IntPtr _handle;
    private GCHandle _callbackRoot;

    public NativeRegistration(Action<uint> onEvent)
    {
      Callback callback = (_, type, _) => { onEvent(type); return 0; };
      _callbackRoot = GCHandle.Alloc(callback);
      try
      {
        var recipient = new Parameters { Callback = Marshal.GetFunctionPointerForDelegate(callback) };
        var error = PowerRegisterSuspendResumeNotification(2, ref recipient, out _handle);
        if (error != 0) throw new Win32Exception((int)error);
      }
      catch { _callbackRoot.Free(); throw; }
    }

    public void Dispose()
    {
      if (_handle == IntPtr.Zero) return;
      var error = PowerUnregisterSuspendResumeNotification(_handle);
      // On failure, keep the delegate rooted: Windows may still invoke it.
      if (error != 0) throw new Win32Exception((int)error);
      _handle = IntPtr.Zero;
      _callbackRoot.Free();
    }
  }
}
