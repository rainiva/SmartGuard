namespace SmartGuard.Configuration;

internal enum PowerSource { Ac, Dc }

/// <summary>Replaceable native surface of the power scheme API. Return value is the Win32 code (0 = success).</summary>
internal interface IPowrProfNative
{
  uint GetActiveScheme(out Guid scheme);
  uint SetActiveScheme(Guid scheme);
  uint ReadFriendlyName(Guid scheme, out string name);
  uint EnumerateSchemes(out Guid[] schemes);
  uint ReadValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, out uint value);
  uint WriteValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, uint value);
}

/// <summary>The only place that imports power scheme functions from PowrProf.dll (PWR-INV-SCHEME-API).</summary>
internal sealed class PowrProfNative : IPowrProfNative
{
  internal static readonly PowrProfNative Instance = new();
  private const uint ErrorNoMoreItems = 259;
  private const uint ErrorMoreData = 234;
  private const uint AccessScheme = 16;
  private const uint MaxSchemes = 256;

  public uint GetActiveScheme(out Guid scheme)
  {
    scheme = Guid.Empty;
    var rc = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
    if (rc != 0) return rc;
    try
    {
      scheme = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(pointer);
      return 0;
    }
    finally
    {
      LocalFree(pointer);
    }
  }

  public uint SetActiveScheme(Guid scheme) => PowerSetActiveScheme(IntPtr.Zero, ref scheme);

  public uint ReadFriendlyName(Guid scheme, out string name)
  {
    name = string.Empty;
    uint size = 0;
    var rc = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
    if (rc != 0) return rc;
    var buffer = new byte[size];
    rc = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
    if (rc != 0) return rc;
    name = System.Text.Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(size, (uint)buffer.Length)).TrimEnd('\0');
    return 0;
  }

  public uint EnumerateSchemes(out Guid[] schemes) => EnumerateBounded(EnumerateAt, MaxSchemes, out schemes);

  private static (uint Rc, Guid Scheme) EnumerateAt(uint index)
  {
    var buffer = new byte[16];
    var size = (uint)buffer.Length;
    var rc = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size);
    return (rc, rc == 0 ? new Guid(buffer) : Guid.Empty);
  }

  // Reads at most `max` schemes. A list is complete only when the API reports ERROR_NO_MORE_ITEMS; an item
  // past the cap means the list was cut, reported as ERROR_MORE_DATA so callers treat the list as unknown
  // instead of concluding that unlisted schemes are absent. At most max + 1 native calls.
  internal static uint EnumerateBounded(Func<uint, (uint Rc, Guid Scheme)> enumerateAt, uint max, out Guid[] schemes)
  {
    var found = new List<Guid>();
    schemes = [];
    for (uint index = 0; ; index++)
    {
      var (rc, scheme) = enumerateAt(index);
      if (rc == ErrorNoMoreItems)
      {
        schemes = found.ToArray();
        return 0;
      }
      if (rc != 0) return rc;
      if (index == max) return ErrorMoreData;
      found.Add(scheme);
    }
  }

  public uint ReadValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, out uint value)
    => source == PowerSource.Ac
      ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
      : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);

  public uint WriteValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, uint value)
    => source == PowerSource.Ac
      ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value)
      : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value);

  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid,
    IntPtr powerSettingGuid, byte[]? buffer, ref uint bufferSize);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid,
    uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
    ref Guid powerSettingGuid, out uint acValueIndex);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
    ref Guid powerSettingGuid, out uint dcValueIndex);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
    ref Guid powerSettingGuid, uint acValueIndex);
  [System.Runtime.InteropServices.DllImport("powrprof.dll")]
  private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
    ref Guid powerSettingGuid, uint dcValueIndex);
  [System.Runtime.InteropServices.DllImport("kernel32.dll")]
  private static extern IntPtr LocalFree(IntPtr memory);
}
