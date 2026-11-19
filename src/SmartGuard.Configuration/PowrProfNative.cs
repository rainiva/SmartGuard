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

/// <summary>Scaffold only; the real P/Invoke implementation arrives with its live tests.</summary>
internal sealed class PowrProfNative : IPowrProfNative
{
  internal static readonly PowrProfNative Instance = new();
  private const uint NotImplemented = 1;

  public uint GetActiveScheme(out Guid scheme) { scheme = Guid.Empty; return NotImplemented; }
  public uint SetActiveScheme(Guid scheme) => NotImplemented;
  public uint ReadFriendlyName(Guid scheme, out string name) { name = string.Empty; return NotImplemented; }
  public uint EnumerateSchemes(out Guid[] schemes) { schemes = []; return NotImplemented; }
  public uint ReadValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, out uint value) { value = 0; return NotImplemented; }
  public uint WriteValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, uint value) => NotImplemented;
}
