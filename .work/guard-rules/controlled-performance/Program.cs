using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;
using SmartGuard.Engine.Worker;

// Bounded measurement spike: no UI automation, no physical power/brightness changes.
var output = Path.GetFullPath(args[0]);
if (Directory.Exists(output)) throw new InvalidOperationException("Use a new output directory; preserve prior evidence.");
Directory.CreateDirectory(output);
var reader = new ForegroundProgramReader();
var observed = reader.Read();
if (!observed.IdentificationSucceeded || observed.ExecutablePath is null)
  throw new InvalidOperationException("Interactive foreground identity unavailable.");
if (Stopwatch.Frequency != TimeSpan.TicksPerSecond)
  throw new InvalidOperationException("Current production coordinator assumes 10MHz timestamps; cannot benchmark this host honestly.");
var target = observed.ExecutablePath;
if (args.Length > 1 && args[1] == "resources-only")
{
  var rows = new List<object>();
  for (var i = 0; i < 5; i++) { OneThread(false); OneThread(true); }
  using var process = Process.GetCurrentProcess();
  foreach (var hooked in new[] { false, true })
  {
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    process.Refresh();
    var baseline = process.HandleCount;
    for (var batch = 1; batch <= 6; batch++)
    {
      for (var i = 0; i < 5; i++) OneThread(hooked);
      process.Refresh();
      var beforeGc = process.HandleCount;
      GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
      await Task.Delay(100);
      process.Refresh();
      rows.Add(new { hooked, cycles = batch * 5, baseline, beforeGc, afterGc = process.HandleCount,
        threads = process.Threads.Count });
    }
  }
  Save("resource-control.json", rows);
  Console.WriteLine("RESOURCE CONTROL COMPLETE");
  return;
}
var matrix = new List<object>();
// Warm JIT and filesystem before recording costs.
await Matrix(1, "cross", "B", -1);
if (args.Length == 1)
{
foreach (var size in new[] { 0, 1, 128 })
foreach (var workload in new[] { "static", "same", "cross", "rapid" })
for (var repeat = 0; repeat < 3; repeat++)
foreach (var mode in repeat % 2 == 0 ? new[] { "A", "B" } : new[] { "B", "A" })
{
  matrix.Add(await Matrix(size, workload, mode, repeat));
  Save("matrix.json", matrix);
}
}

var realtime = new List<object>();
if (args.Length < 2 || args[1] != "stationary-only")
{
for (var repeat = 0; repeat < 3; repeat++)
foreach (var phase in new[] { 0.5, 7.5, 14.5 })
foreach (var mode in repeat % 2 == 0 ? new[] { "A", "B" } : new[] { "B", "A" })
{
  realtime.Add(await RealTime(mode, phase, repeat));
  Save("realtime.json", realtime);
}
}

var stationary = new List<object>();
for (var repeat = 0; repeat < 3; repeat++)
foreach (var mode in repeat % 2 == 0 ? new[] { "A", "B" } : new[] { "B", "A" })
{
  stationary.Add(await Stationary(mode, repeat));
  Save("stationary.json", stationary);
}
var resources = new List<object>();
using (var process = Process.GetCurrentProcess())
{
  for (var i = 0; i < 30; i++)
  {
    using (var listener = new ForegroundEventListener(() => { }))
    {
      listener.Start();
      await Task.Delay(50);
      if (!listener.IsHealthy) throw new InvalidOperationException(listener.LastFailure);
      listener.Stop();
    }
    if (i % 5 == 4)
    {
      process.Refresh();
      resources.Add(new { cycles = i + 1, handles = process.HandleCount, threads = process.Threads.Count });
    }
  }
}
Save("resources.json", resources);
Save("receipt.json", new { completedUtc = DateTimeOffset.UtcNow, target, timestampFrequency = Stopwatch.Frequency,
  matrixCases = matrix.Count, realtimeCases = realtime.Count, stationaryCases = stationary.Count,
  energy = "NOT MEASURED", physicalPowerChanges = 0 });
Console.WriteLine("MEASUREMENT COMPLETE");

async Task<object> Matrix(int size, string workload, string mode, int repeat)
{
  var clock = new VirtualTime();
  var name = $"matrix-{size}-{workload}-{mode}-{repeat}";
  using var rig = new Rig(Path.Combine(output, name), size, target, clock, reader);
  rig.Fact = ForegroundObservation.Identified(workload is "static" or "same" ? target : @"C:\sgbench\outside.exe");
  await rig.Cycle();
  rig.ResetCounts();
  using var process = Process.GetCurrentProcess();
  var cpu = process.TotalProcessorTime;
  var allocation = GC.GetTotalAllocatedBytes(false);
  var elapsed = Stopwatch.StartNew();
  // 120 simulated seconds with a 100ms input grid, same workload and phase in A/B.
  for (var step = 1; step <= 1200; step++)
  {
    clock.Advance(TimeSpan.FromMilliseconds(100));
    var signal = workload == "same" ? step % 10 == 0
      : workload == "cross" ? step % 100 == 0
      : workload == "rapid" && step <= 900;
    if (signal)
    {
      rig.Fact = ForegroundObservation.Identified(workload == "same" || step % (workload == "rapid" ? 2 : 200) == 0
        ? target : @"C:\sgbench\outside.exe");
      if (mode == "B") rig.Coordinator.MarkForegroundSignal();
    }
    // A stable final target at 90s checks that rapid history does not lose the final state.
    if (workload == "rapid" && step == 900) rig.Fact = ForegroundObservation.Identified(target);
    await rig.Cycle();
    rig.Drain();
  }
  process.Refresh();
  return new { size, workload, mode, repeat, simulatedSeconds = 120, elapsedMs = elapsed.Elapsed.TotalMilliseconds,
    cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
    allocatedBytes = GC.GetTotalAllocatedBytes(false) - allocation,
    counts = rig.Counts(), finalAdmitted = rig.State.LastForegroundAdmitted };
}

async Task<object> RealTime(string mode, double phase, int repeat)
{
  using var rig = new Rig(Path.Combine(output, $"realtime-{repeat}-{phase}-{mode}"), 128, target, TimeProvider.System, reader);
  rig.Fact = ForegroundObservation.Identified(@"C:\sgbench\outside.exe");
  await rig.Cycle();
  rig.ResetCounts();
  var watch = Stopwatch.StartNew();
  var switched = false;
  double? changed = null;
  long changedTick = 0;
  double? admitted = null;
  while (watch.Elapsed.TotalSeconds < 16.2)
  {
    if (!switched && watch.Elapsed.TotalSeconds >= phase)
    {
      switched = true;
      changed = watch.Elapsed.TotalMilliseconds;
      changedTick = Stopwatch.GetTimestamp();
      rig.Fact = ForegroundObservation.Identified(target);
      if (mode == "B") rig.Coordinator.MarkForegroundSignal();
    }
    await rig.Cycle();
    if (rig.State.LastForegroundAdmitted == true) admitted ??= watch.Elapsed.TotalMilliseconds;
    var deadline = TimeSpan.FromSeconds(switched ? 16.2 : phase) - watch.Elapsed;
    var delay = rig.Coordinator.ComputeWaitDelay();
    if (deadline < delay) delay = deadline;
    if (delay > TimeSpan.Zero) await rig.Wake.WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(delay.TotalMilliseconds))));
    rig.Drain();
  }
  if (admitted is null) throw new InvalidOperationException("Final stable admission was not observed.");
  double? Latency(long? tick) => tick is null ? null : Stopwatch.GetElapsedTime(changedTick, tick.Value).TotalMilliseconds;
  return new { mode, phase, repeat, readLatencyMs = Latency(rig.FirstTargetRead),
    actionLatencyMs = Latency(rig.FirstApply), completionLatencyMs = admitted - changed, counts = rig.Counts(),
    scope = "Controlled injected fact/signal, actual Reader cost and Runner/status IO; not OS delivery latency" };
}

async Task<object> Stationary(string mode, int repeat)
{
  using var rig = new Rig(Path.Combine(output, $"stationary-{repeat}-{mode}"), 128, target, TimeProvider.System, reader);
  rig.UseActualFact = true;
  await rig.Cycle();
  rig.ResetCounts();
  var events = 0;
  using var listener = mode == "B" ? new ForegroundEventListener(() =>
  {
    Interlocked.Increment(ref events);
    rig.Coordinator.MarkForegroundSignal();
  }) : null;
  listener?.Start();
  await Task.Delay(200);
  using var process = Process.GetCurrentProcess();
  process.Refresh();
  var cpu = process.TotalProcessorTime;
  var handles = process.HandleCount;
  var threads = process.Threads.Count;
  var memory = process.PrivateMemorySize64;
  var watch = Stopwatch.StartNew();
  while (watch.Elapsed.TotalSeconds < 30)
  {
    await rig.Cycle();
    var delay = rig.Coordinator.ComputeWaitDelay();
    var left = TimeSpan.FromSeconds(30) - watch.Elapsed;
    if (left < delay) delay = left;
    // WaitAsync truncates sub-millisecond delays. Round this measurement wait up so the
    // artificial end-of-sample boundary cannot generate thousands of zero-duration iterations.
    if (delay > TimeSpan.Zero) await rig.Wake.WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(delay.TotalMilliseconds))));
    rig.Drain();
  }
  process.Refresh();
  var result = new { mode, repeat, seconds = watch.Elapsed.TotalSeconds, events,
    uncontaminated = events == 0, listenerHealthy = listener?.IsHealthy, cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
    handlesStart = handles, handlesEnd = process.HandleCount, threadsStart = threads, threadsEnd = process.Threads.Count,
    privateStart = memory, privateEnd = process.PrivateMemorySize64, counts = rig.Counts() };
  listener?.Stop();
  return result;
}

void Save(string name, object value) => File.WriteAllText(Path.Combine(output, name),
  JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static void OneThread(bool hooked)
{
  if (hooked)
  {
    using var listener = new ForegroundEventListener(() => { });
    listener.Start();
    Thread.Sleep(50);
    if (!listener.IsHealthy) throw new InvalidOperationException(listener.LastFailure);
    listener.Stop();
  }
  else
  {
    var thread = new Thread(() => { });
    thread.Start();
    thread.Join();
  }
}

sealed class VirtualTime : TimeProvider
{
  private long _ticks;
  public override long TimestampFrequency => TimeSpan.TicksPerSecond;
  public override long GetTimestamp() => _ticks;
  public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
  public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
}

sealed class Rig : IDisposable
{
  public ForegroundObservation Fact = ForegroundObservation.Unknown;
  public bool UseActualFact;
  public long? FirstTargetRead, FirstApply;
  public readonly GuardianLoopIterationState State = new() { PlanCatalog = new Dictionary<Guid, string>() };
  public readonly SemaphoreSlim Wake = new(0);
  public readonly GuardianCycleCoordinator Coordinator;
  private readonly PowerLifecycleDirective _directive;
  private int _reader, _full, _power, _battery, _bright, _status, _log, _apply, _cycles;
  private Guid _active;
  private readonly string _statusPath;
  private DateTime _lastWrite;
  public Rig(string root, int size, string target, TimeProvider time, ForegroundProgramReader reader)
  {
    Directory.CreateDirectory(root);
    var config = GuardConfig.CreateDefault(root);
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = Enumerable.Range(0, Math.Max(0, size - 1)).Select(i => $@"C:\sgbench\filler-{i}.exe").ToList();
    if (size > 0) config.ForegroundProgramPaths.Add(target); // Worst-case final-entry match.
    config.HeartbeatIntervalMin = 0;
    config.BrightnessRetryCount = 0;
    _active = config.BalancedPlanGuid;
    _statusPath = Path.Combine(root, "status.json");
    ForegroundObservation Read()
    {
      _reader++;
      var actual = reader.Read();
      if (!actual.IdentificationSucceeded) throw new InvalidOperationException("Real Reader failed during measurement.");
      var fact = UseActualFact ? actual : Fact;
      if (fact.ExecutablePath == target) FirstTargetRead ??= Stopwatch.GetTimestamp();
      return fact;
    }
    var runner = new GuardianIterationRunner(new IdleTracker(time), new BrightnessService(), new StatusPublisher(_statusPath, time),
      () => GuardConfigReadResult.FromConfig(config),
      (_, _, _, _) => { _log++; return GuardianLogWriteResult.AcceptedByPipeline; },
      _ => { }, false, new HeartbeatSchedule(time),
      readIdleSeconds: () => 0, readBatteryInfo: _ => { _battery++; return (90, true); },
      readCurrentPlan: () => { _power++; return new(true, new PowerSchemeInfo(_active, null), PowerPlanFailureKind.None, ""); },
      applyPlan: (plan, _) => { FirstApply ??= Stopwatch.GetTimestamp(); _apply++; _active = plan; return Task.FromResult(new PowerPlanApplyResult(true, plan, plan, PowerPlanFailureKind.None, "")); },
      timeProvider: time, readForeground: Read, readBrightness: () => { _bright++; return -1; });
    Coordinator = new GuardianCycleCoordinator(() => GuardConfigReadResult.FromConfig(config), Read, State, Wake,
      async (context, token) =>
      {
        _full++;
        await runner.RunAsync(State, context, token);
        var modified = File.GetLastWriteTimeUtc(_statusPath);
        if (modified != _lastWrite) { _status++; _lastWrite = modified; }
        // Virtual replay compresses minutes into milliseconds. Space its physical writes rather
        // than treating an artificial burst of replacements as the production IO workload.
        if (time is VirtualTime) await Task.Delay(100, token);
      }, time);
    _directive = new PowerLifecycleStateMachine(time).TakeDirective();
  }
  public Task Cycle() { _cycles++; return Coordinator.RunCycleAsync(_directive, 15, CancellationToken.None); }
  public void Drain() { while (Wake.Wait(0)) { } }
  public void ResetCounts() { _reader = _full = _power = _battery = _bright = _status = _log = _apply = _cycles = 0; }
  public object Counts() => new { reader = _reader, full = _full, power = _power, battery = _battery,
    brightness = _bright, status = _status, log = _log, apply = _apply, cycles = _cycles };
  public void Dispose() => Wake.Dispose();
}
