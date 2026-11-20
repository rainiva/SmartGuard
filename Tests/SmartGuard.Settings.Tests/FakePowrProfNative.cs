using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

internal sealed class FakePowrProfNative : IPowrProfNative
{
  private readonly object _sync = new();
  private readonly List<string> _calls = [];

  public Func<(uint Rc, Guid Scheme)> GetActive { get; set; } = () => (0, PowerPlanCatalogProvider.BalancedPlanGuid);
  public Func<Guid, uint> SetActive { get; set; } = _ => 0;
  public Func<Guid, (uint Rc, string Name)> ReadName { get; set; } = scheme => (0, scheme.ToString("D"));
  public Func<(uint Rc, Guid[] Schemes)> Enumerate { get; set; } = () => (0, []);
  public Func<Guid, Guid, PowerSource, (uint Rc, uint Value)> ReadValue { get; set; } = (_, _, _) => (0, 50);
  public Func<Guid, Guid, PowerSource, uint, uint> WriteValue { get; set; } = (_, _, _, _) => 0;

  public IReadOnlyList<string> Calls { get { lock (_sync) return _calls.ToArray(); } }

  private void Record(string call) { lock (_sync) _calls.Add(call); }

  public uint GetActiveScheme(out Guid scheme) { Record("get-active"); var r = GetActive(); scheme = r.Scheme; return r.Rc; }
  public uint SetActiveScheme(Guid scheme) { Record($"set-active {scheme:D}"); return SetActive(scheme); }
  public uint ReadFriendlyName(Guid scheme, out string name) { Record($"read-name {scheme:D}"); var r = ReadName(scheme); name = r.Name; return r.Rc; }
  public uint EnumerateSchemes(out Guid[] schemes) { Record("enumerate"); var r = Enumerate(); schemes = r.Schemes; return r.Rc; }

  public uint ReadValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, out uint value)
  {
    Record($"read-value {scheme:D} {subgroup:D} {setting:D} {source}");
    var r = ReadValue(scheme, setting, source);
    value = r.Value;
    return r.Rc;
  }

  public uint WriteValueIndex(Guid scheme, Guid subgroup, Guid setting, PowerSource source, uint value)
  {
    Record($"write-value {scheme:D} {subgroup:D} {setting:D} {source} {value}");
    return WriteValue(scheme, setting, source, value);
  }
}
