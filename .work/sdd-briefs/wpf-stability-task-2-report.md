# WPF stability Task 2 report

## Status

`BLOCKED` — the fail-closed isolated evidence controller is implemented and its focused checks passed, but Task 2's reproduction/attribution gate is not closed. The one contamination attempt completed successfully rather than reproducing a failure; no actual `testhost` PID or attributable stack/counter/trace was captured. `production-equivalent` is deliberately blocked because no existing WPF selector can prove that hooks and artificial worker blocking are disabled.

## Changed files

- `Tests/SmartGuard.Settings.StabilityProbe/SmartGuard.Settings.StabilityProbe.csproj` (new)
- `Tests/SmartGuard.Settings.StabilityProbe/Program.cs` (new)
- `scripts/Invoke-WpfStabilityProbe.ps1` (new)
- `scripts/Invoke-SmartGuardDotNetTests.ps1`
- `Tests/SmartGuard.Tests.ps1`
- This report

No `src/` production file was modified.

## RED evidence

Command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester -Path '.\Tests\SmartGuard.Tests.ps1' -FullNameFilter '*WPF stability probe*' -PassThru"
```

The sandboxed run was infrastructure-blocked because Pester could not create its temporary HKCU TestRegistry key (Pester `Failed 0/2`; shell exit `0`). The same command in approved user context produced the required RED: Pester `Failed 2/2`, `NotRun 122`, shell exit `0`; failures were the absent probe project and missing `Read-SmartGuardWpfStabilityProbeResult` entry point.

## GREEN evidence

- `dotnet restore .\Tests\SmartGuard.Settings.StabilityProbe\SmartGuard.Settings.StabilityProbe.csproj --nologo`: exit `0`.
- `dotnet build .\Tests\SmartGuard.Settings.StabilityProbe\SmartGuard.Settings.StabilityProbe.csproj --no-restore --nologo -v q`: exit `0`; `0` warnings, `0` errors.
- Targeted WPF probe Pester command above: `Passed 3`, `Failed 0`, `NotRun 122`, shell exit `0`. It exercises malformed/missing/zero-result rejection plus actual isolated non-zero and timeout child processes.
- Existing strict-TRX regression: `*missing or malformed TRX*` => `Passed 1`, `Failed 0`, `NotRun 124`, shell exit `0`.
- Real contamination attempt:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WpfStabilityProbe.ps1 -Mode test-contamination -Iterations 1 -TimeoutSeconds 60 -Seed 20260823 -Filter 'FullyQualifiedName~WpfStaTestHostTests'
  ```

  Exit `0`; child exit `0`; TRX `Completed`/`Passed`, `TotalCount=6`, `TimedOut=False`. Evidence directory: `.work/test-results/wpf-stability/20260823T080951561Z-i1-s20260823-a83a5b58d2b04988b7ab84bf76ae625e/`. The existing Settings-project build emitted 7 pre-existing analyzer/nullability warnings and 0 errors.
- Explicit production-equivalent fail-closed check:

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WpfStabilityProbe.ps1 -Mode production-equivalent -Iterations 1 -TimeoutSeconds 60 -Seed 20260824
  ```

  Script exit `1` as intended; controller exit `0`, result `Blocked`, and its reason records the missing no-hook/no-artificial-blocking selector. It did not execute contamination tests.

## Diagnostic availability

`dotnet-counters`, `dotnet-stack`, and `dotnet-trace` were all unavailable. Both `capture-status.json` files record `Unavailable` with an explicit no-install reason; no empty trace placeholder was produced. The controller records the direct child PID; the actual test-host PID was unresolved before exit, so capture records that fact and does not claim stack/counter evidence.

## Self-review and concerns

- Process timeout calls `taskkill /PID <exact child PID> /T /F` and waits for termination; non-zero child exit and timeout remain distinct `Failed` results.
- Passed results require a nonzero, completed, failure-free TRX; malformed/missing/zero evidence fails closed in the shared reader.
- The run directory is fresh and rejects reuse.
- No retry, timeout inflation, production scheduling change, or contamination fallback is present.
- Task 2 remains blocked: there is no 5/5 controlled reproduction and no attributable diagnostics. Full `Run-Tests.ps1` was not run in this time-bounded handoff.

## Commit

No commit was created. The parent requested that incomplete/blocked work not be committed.

## Exit-code propagation test correction (2026-08-23)

Independent fresh Pester observed the real non-zero-child assertion as `ExitCode=1` rather than `23`. Its PowerShell comparison isolated the cause: direct `Start-Process -ArgumentList` receives one already parsed command line, while the probe's `ArgumentList.Add(command)` performs a second command-line encoding. The old self-test value started with a quoted executable path (`"%SystemRoot%\System32\cmd.exe" /d /c exit 23`), so it was not a valid arbitrary command string for this interface.

The Pester self-test now passes the legal outer-`cmd` command `exit /b 23`. No generic arbitrary-command parser was added, and the interim `/S` controller change was removed because it did not address the double-encoding boundary.

Evidence:

- RED: the independent fresh run of `*WPF stability probe*` reported `Passed 2`, `Failed 1`, `NotRun 122`; the failed behavior was the incorrect `ExitCode=1` from the invalid quoted test input. The local current run could not reproduce that environment-sensitive branch before the test correction (it returned `23`), so this is recorded as independent evidence rather than relabeled as local RED.
- GREEN: after changing only the test command, the same Pester filter reported `Passed 3`, `Failed 0`, `NotRun 122` (shell exit `0`); it still exercises a real non-zero outer child and real timeout child.
- `dotnet build .\Tests\SmartGuard.Settings.StabilityProbe\SmartGuard.Settings.StabilityProbe.csproj --no-restore --nologo -v q`: exit `0`, `0` warnings, `0` errors.

This corrects the probe test interface only; it does not close the Task 2 reproduction or diagnostic blockers above.

## Live diagnostics integration attempt (2026-08-23)

Status remains `BLOCKED`. The user-provided isolated tool directory is now the explicit `ToolRoot` parameter and the post-parameter default is `D:\Project\SmartGuard\.work\diagnostics\dotnet-tools`; it no longer references `$PSScriptRoot` during parameter binding. The installed versions were verified as `9.0.661903+d7b455b46332b31fd9ba3a3f3e020387984c511a` for `dotnet-counters`, `dotnet-stack`, and `dotnet-trace`.

The script now has a bounded controller-tree resolver for actual process names `testhost`/`testhost.x86`, never substitutes the controller PID, and has real launch definitions for counters, stack, and trace. Every capture record includes tool path, command, exit code, stdout/stderr paths, artifact path, and a reason. A tool that is absent, cannot attach, has a nonzero exit, or fails to create its expected nonempty artifact makes the probe fail closed.

TDD evidence:

- RED: after adding the ToolRoot/PID/tool-launch contracts, focused Pester reported `Passed 2`, `Failed 1`, `NotRun 122`; the old binding-time `$PSScriptRoot` default was the only failed contract.
- GREEN: after making `ToolRoot` nullable/empty in the param block and resolving it after binding, focused Pester reported `Passed 3`, `Failed 0`, `NotRun 122` (shell exit `0`).

Live smoke attempt, using the default ToolRoot:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-WpfStabilityProbe.ps1 -Mode test-contamination -Iterations 1 -TimeoutSeconds 60 -Seed 20260826
```

The default resolved correctly and the tool paths in `capture-status.json` point to the installed directory. The controller child nevertheless ended in under one second with `ExitCode=1`, missing TRX, and no testhost PID. The run is `.work/test-results/wpf-stability/20260823T110152235Z-i1-s20260826-bd03a03419ef4c30bd154be35259115e/`; all three tools are correctly `Blocked`, with null command/exit code and no empty trace/counter artifact. Therefore no live tool invocation or 5-iteration run was attempted: the controller's newly introduced outer command-string encoding fails before a real WPF test host exists. Repairing that safely requires a structured controller launch/argument boundary rather than another quoting switch, which is beyond this time-bounded handoff.

## Latest verification after diagnostic-tool authorization (2026-08-23)

The preceding live-diagnostics section is superseded by this section. The user authorized installation; fixed `9.0.661903` tools are isolated under `.work/diagnostics/dotnet-tools/` and are not project dependencies or a `Run-Tests.ps1` requirement.

Additional RED evidence found and fixed before the current run:

- The script's `$PSScriptRoot` default was evaluated during parameter binding and failed under Windows PowerShell; the default is now resolved after binding.
- The first outer controller launch used nested command-string quoting and produced `MSB1006` before `testhost`; the controller now launches `dotnet` with a structured, quoted argument list and stores the inner `cmd.exe` command in a UTF-8 command file.
- `.NET ProcessStartInfo.ArgumentList` is unavailable in the Windows PowerShell host used by the gate; the launcher now uses `Start-Process -ArgumentList` for the controller and `ProcessStartInfo.Arguments` for diagnostic tools.
- PowerShell's read-only `$PID` collided with diagnostic script-block parameters; target process parameters now use a non-reserved name.
- A timeout cleanup race returned `taskkill` exit `255` after the root had already exited; the controller now treats that exact already-dead state as successful cleanup and still fails if the root remains alive.
- `dotnet-trace --duration 00:00:01` could finish its artifact while returning `2` when the short-lived target exited before the stop command; the bounded trace window is now `250ms`, and nonzero tool exits remain failures.

Fresh evidence:

- Probe build: `dotnet build .\Tests\SmartGuard.Settings.StabilityProbe\SmartGuard.Settings.StabilityProbe.csproj --no-restore --nologo -v q` => exit `0`, warnings `0`, errors `0`.
- Focused Pester `*WPF stability probe*` => `Passed 3`, `Failed 0`, `Skipped 0`, `NotRun 122`, shell exit `0`.
- Five fresh contamination iterations (`Seed 20260823`, `FullyQualifiedName~WpfStaTestHostTests`) all had controller exit `0`, child exit `0`, TRX `Completed`/`Passed`, `TotalCount=6`, `TimedOut=False`, and a resolved actual `testhost` PID. Each run captured nonempty artifacts from `dotnet-counters`, `dotnet-stack`, and `dotnet-trace`, with tool exit `0` and `CaptureStatus=Passed`.

The five run directories are:

```text
.work/test-results/wpf-stability/20260823T111745497Z-i1-s20260823-8a61fc1a676d4d25bd948705924caabd/
.work/test-results/wpf-stability/20260823T111748616Z-i2-s20260823-6f8eeb310c624ba5abc6754214816f8c/
.work/test-results/wpf-stability/20260823T111751691Z-i3-s20260823-47ec86a5c24542ad83cb798725fd494a/
.work/test-results/wpf-stability/20260823T111754991Z-i4-s20260823-6bf65aa1cc8649009a13e512741f5795/
.work/test-results/wpf-stability/20260823T111758278Z-i5-s20260823-5f6cc78eabac46de9127af27d6b5c6d8/
```

This is **not** a reproduction: the actual child test did not fail in any of the five runs. The captured stack is therefore a passing-run snapshot of `testhost`/xUnit execution, not attribution of the suspected `paged-queued` failure. Task 2 remains `BLOCKED` under the hard stop: no failed 5/5 contamination or production-equivalent run and no failure-time stack/counter attribution. `production-equivalent` remains explicitly `Blocked` because the repository has no selectable path that proves test hooks and artificial worker blocking are disabled. No production scheduling file was changed and no Task 2 commit was created.

## Review fixes and fresh verification (2026-08-23)

The fresh-context review identified evidence-contract gaps; they were treated as blocking and fixed before the latest run:

- `Read-SmartGuardWpfStabilityProbeResult` now requires `ExecutionStatus`, `ReproductionStatus`, `TestOrderPath`, `CleanupTimedOut`, and `CleanupError`; a `Passed` JSON with nonzero child exit is rejected, and the TRX is reparsed from `TrxPath` before acceptance.
- The controller writes `test-order.json` from TRX `UnitTestResult` order and the reader requires a captured, nonempty, contiguous ordinal/name list consistent with mode, iteration, seed, filter, and total count.
- Execution health (`ExecutionStatus=Passed`) is distinct from conclusion (`ReproductionStatus=NotReproduced`); a passing contamination run is machine-readable as not reproduced, not as a root-cause reproduction.
- Timeout cleanup has a five-second grace bound for `taskkill`, process termination, and output drain; failures become explicit cleanup evidence rather than an unbounded wait.
- Diagnostic entries record `CapturePhase`; when the controller reaches its deadline, a `timeout-pre-terminate` capture is attempted before the exact process tree is terminated, with an explicit failure reason when the actual testhost PID is unavailable.

Fresh post-review verification:

- Probe build: exit `0`, warnings `0`, errors `0`.
- Focused WPF probe Pester: `Passed 3`, `Failed 0`, `NotRun 122`, shell exit `0`.
- Five new contamination runs (`20260823T113108279Z` through `20260823T113117855Z`) each had `ExecutionStatus=Passed`, `ReproductionStatus=NotReproduced`, child exit `0`, `TotalCount=6`, six ordered test names, a real testhost PID, and three nonempty diagnostic artifacts with exit `0`; aggregate `PASS5=5`.

These results strengthen the evidence plumbing but do not change the gate conclusion: no failure occurred, so no failure-time attribution exists and Task 2 remains `BLOCKED`. The latest run directories and their `probe-result.json`, `test-order.json`, `capture-status.json`, TRX, stack, counters, and trace artifacts are retained under `.work/test-results/wpf-stability/`.

## Deterministic-selection and bounded-handoff verification (2026-08-23)

This section supersedes the earlier diagnostic-availability and three-test-count notes above.

- The user-authorized tools remain isolated at `.work/diagnostics/dotnet-tools/`, fixed at `9.0.661903+d7b455b46332b31fd9ba3a3f3e020387984c511a` for `dotnet-counters`, `dotnet-stack`, and `dotnet-trace`.
- Every run now performs bounded `dotnet test --list-tests` discovery before starting the controller. The sorted six-test selection is stored in the controller capture directory and passed to the C# controller; a filter string is no longer accepted as a substitute for a test-order list. A missing/corrupt/empty selection manifest is rejected for failed, timed-out, and blocked results.
- `RequireReproduction` now has explicit exit semantics: `0` only when every run contains complete `Reproduced` failure evidence with capture success; `2` when the run is healthy but the suspected failure is not reproduced; `1` for controller, result, cleanup, or diagnostic evidence failure. A passing contamination run remains `NotReproduced` and does not become green for the reproduction gate.
- Timeout diagnostics share one `CaptureDeadlineUtc` anchored at the controller timeout plus its five-second handoff. Each tool records its phase, deadline, exit code, artifact and named failure; the outer controller is not allowed to wait past that shared handoff.

Fresh verification:

- Probe build: exit `0`, warnings `0`, errors `0`.
- Focused WPF probe Pester: `Passed 4`, `Failed 0`, `NotRun 122`, shell exit `0`.
- Real contamination smoke: `.work/test-results/wpf-stability/20260823T120244519Z-i1-s20260828-2d1dc5f6dd9443229f0277cb6a4557a3/`; controller/child exit `0`, TRX `Passed`, `TotalCount=6`, `ReproductionStatus=NotReproduced`, actual testhost PID `24644`, counters/stack/trace nonempty, all tool exits `0`, `CaptureStatus=Passed`.
- Production-equivalent smoke: `.work/test-results/wpf-stability/20260823T120014355Z-i1-s20260827-567fc622e36141b2a2bdace62330b955/`; selection list captured, result explicitly `Blocked` because no selector proves hooks and artificial worker blocking are disabled, and diagnostic entries explicitly `Blocked` because no testhost exists.
- Fresh full gate: Pester `126/126`, .NET `1542/1542`; Tray Integration was `BLOCKED` by the elevated Windows PowerShell handoff being canceled (`The operation was canceled by the user`), and Installer Integration was `NOT RUN` as an upstream consequence. `Run-Tests.ps1` therefore returned exit `1`; this is not reported as full-green or as a WPF code failure. Summary: `.work/test-results/run-tests-20260823T120308036Z-04788bb2b4354168867998f94005ff27.summary.json`.

Task 2 remains `BLOCKED`: no 5/5 failure reproduction, no failure-time attribution, and no legal production-equivalent selector. No production scheduling file was changed and no commit was created.

## Final adversarial closure of Task 2 plumbing (2026-08-23)

The final review found no Critical or Important findings. A forged `ReproductionStatus=Reproduced` payload is now rejected unless it is a failed `test-contamination` run with nonzero child exit, no timeout/cleanup failure, a captured order manifest, a nonempty failed TRX, and the `paged-queued` signature in child stdout/stderr. The reproduction exit helper also rejects missing/invalid fields, and discovery timeout cleanup waits for the root process within a bounded grace. Focused Pester remains `4/4`; probe build remains `0` warnings/`0` errors.

The latest real smoke is `.work/test-results/wpf-stability/20260823T121532573Z-i1-s20260829-1427e9a00ca644b3b872503ced28a32c/`: six tests passed, `ReproductionStatus=NotReproduced`, actual diagnostics captured successfully. An explicit `-RequireReproduction` run on seed `20260830` returned nonzero while the healthy run remained `NotReproduced`, as required. These are evidence-contract results, not a root-cause closure; Task 2 stays `BLOCKED` pending failure-time reproduction or a legal production-equivalent selector.

## Post-install authorization and final-boundary verification (2026-08-23)

The user authorized installation of diagnostic tools. The isolated directory `.work/diagnostics/dotnet-tools/` contains `dotnet-counters.exe`, `dotnet-stack.exe`, and `dotnet-trace.exe`, all verified at version `9.0.661903+d7b455b46332b31fd9ba3a3f3e020387984c511a`. They are not project dependencies and do not alter `Run-Tests.ps1` requirements.

Fresh verification after the final timeout-boundary change:

- `dotnet build .\Tests\SmartGuard.Settings.StabilityProbe\SmartGuard.Settings.StabilityProbe.csproj --no-restore --nologo -v q` => exit `0`, warnings `0`, errors `0`.
- Focused Pester `*WPF stability probe*` in the approved user context => `Passed 4`, `Failed 0`, `Skipped 0`, `NotRun 122`, shell exit `0`. A sandboxed attempt was separately classified as infrastructure-blocked because Pester could not write its temporary HKCU registry key.
- One healthy contamination smoke (`Seed 20260833`) => controller exit `0`, child exit `0`, TRX `Passed`, `TotalCount=6`, `ReproductionStatus=NotReproduced`; the actual testhost was observed in `capture-status.json` and all three diagnostic tools produced nonempty artifacts with exit `0`. Evidence directory: `.work/test-results/wpf-stability/20260823T122736188Z-i1-s20260833-bb0ad9db82324d77abb1ff81ab0a90c8/`.
- One deliberate one-second timeout regression (`Seed 20260832`) now leaves a complete bounded receipt rather than a missing result: `Status=Failed`, `TimedOut=True`, `CleanupTimedOut=False`, `TrxStatus=Passed`, and explicit capture status/blocked reasons. It is a timeout/infrastructure case, not a reproduced `paged-queued` failure. Evidence directory: `.work/test-results/wpf-stability/20260823T122404931Z-i1-s20260832-654c419d68f34349b58624a925c63c90/`.

These results verify the diagnostic plumbing and bounded cleanup, not the suspected root cause. No failure-time `paged-queued` evidence exists; `production-equivalent` remains explicitly `Blocked`; no production scheduling file was changed and no commit was created. Task 2 therefore remains `BLOCKED`.

## Fresh full-gate RED and owner attribution (2026-08-23)

The next elevated full gate produced a real WPF failure; the earlier statement that no failure had been observed is superseded by this section.

- Summary: `.work/test-results/run-tests-20260823T123758579Z-55e98de61a4a45358ffd3af7af2f432a.summary.json`.
- Pester: `126/126` passed.
- .NET: `1541/1542` passed, `1` failed. The failed test is `SmartGuard.Settings.Tests.SettingsWindowControllerTests.User_clicks_refresh_button_and_sees_new_log_lines`.
- Tray Integration: `NOT RUN`; Installer Integration: `NOT RUN` because the upstream .NET layer failed. The parent exit code is `1`; this is not a full-green gate.
- TRX evidence: `.work/test-results/0080d122911740ffa94e5843d407e1c6-SmartGuard.Settings.Tests.trx`. The failure waited 10,000 ms with `flight stage=paged-queued+10006ms`, `active refresh=1`, `global flights running=1`, `started=137`, `pending=18`, `log read hook=False`, and zero log items. This proves the paged flight did not obtain its worker; it does not by itself prove which queued work item was first.

Source and live-diagnostic evidence now identify a stronger owner candidate:

1. `SettingsLogRefreshPipeline.StartPagedFlight` queues an outer `Task.Run`; `LogQueryService.ReadPreviewPageAsync`, `BuildPageIndexAsync`, and `ReadPageAsync` each queue another `Task.Run`. This is nested shared-ThreadPool scheduling.
2. `SettingsPlanCatalogCoordinator.BeginLoadPlanCatalogAsync` starts an unowned fire-and-forget `Task.Run(() => PowerPlanCatalogProvider.LoadWithRetry())`. Its per-window CTS is never passed to `LoadWithRetry`; `Dispose` can cancel the CTS but cannot cancel or await the already running synchronous `powercfg`/retry work.
3. `PowerPlanCatalogProvider.LoadWithRetry` can perform multiple synchronous `powercfg` calls (up to 15 seconds each) and sleeps between attempts. It therefore holds shared ThreadPool workers when launched by the coordinator.
4. The user-authorized long diagnostic run `.work/diagnostics/settings-suite-20260823T124905458Z-537d542e719d44249e49a4c9187ab7f4/diagnostic-summary.json` completed all 546 Settings tests, but repeated `dotnet-stack` snapshots show many `SettingsPlanCatalogCoordinator+<>c.<BeginLoadPlanCatalogAsync>b__14_0()` frames on ThreadPool workers. This is live pressure evidence, not a failure-time stack.

Classification:

- **Confirmed:** the full gate has a real `paged-queued` RED; the log-read blocking hook was false; the current test workaround raises ThreadPool minimum workers; plan-catalog loads are fire-and-forget and not cancellable through their coordinator CTS; the long run observed those coordinator workers.
- **Reasonable hypothesis:** unowned plan-catalog loads contaminate the shared ThreadPool and amplify the nested paging queues, leaving the page flight queued. This explains the failure better than a pure log-file or Dispatcher fault, but is not yet a failure-time causal proof.
- **Not yet verified:** the exact first starving task in the failed process; whether a production-equivalent run with plan-catalog work disabled still fails; whether nested paging `Task.Run` is independently necessary after the catalog owner is corrected.

Task 2 now has a real RED, but the original Task 3 scope is wrong: its listed files are test-host/fixture files, while the strongest owner candidate is `src/SmartGuard.Settings/SettingsPlanCatalogCoordinator.cs` plus the cancellation boundary in `src/SmartGuard.Configuration/PowerPlanCatalogProvider.cs`. Do not apply Task 3 as written. The next gate is a bounded owner-scoped RED for catalog task cancellation/observation and disposal; production paging changes remain out of scope until that test is green and a controlled rerun shows whether `paged-queued` survives.

## Owner-scoped repair and final verification (2026-08-23)

The owner-scoped RED is now GREEN. The coordinator owns cancellation/generation and suppresses late UI apply after `Dispose`; the async catalog loader has a cancellable `powercfg` path, one shared load slot, and cache reuse. The old WPF fixture `ThreadPool.SetMinThreads(128)` workaround was removed. The paging query implementation, heartbeat/log writer, idle detection, and business power-plan semantics were not changed in this slice.

RED evidence included late apply after dispose, non-idempotent dispose, missing async loader symbols, concurrent catalog calls (`maxActive=2`), duplicate uncached calls (`calls=2`), and the fixture thread-pool-floor contract. Each was turned GREEN by the narrow owner or test-harness change; an initial stability-script exit-code bug was also fixed before accepting its results.

Verification:

- Coordinator `2/2`; WPF host contract + coordinator `9/9`; provider `11/11`.
- Full Settings suite without the global thread-pool floor: `549/549`.
- Long diagnostic run: `549/549`; queue length maximum `1`, final `0`; no post-fix coordinator/catalog/powercfg frames in six stack snapshots. Evidence: `.work/diagnostics/settings-suite-20260823T132507442Z-2250439a43ee473ab86a951ef2fc1e26/diagnostic-summary.json`.
- Ten consecutive Settings runs: all `549/549`, exit `0`, `AllPassed=true`. Evidence: `.work/diagnostics/settings-stability-20260823T133641057Z-4d7995baee514d68a8aedabc3dbef529/summary.json`.
- Three elevated full gates, each exit `0`: Pester `126/126`, .NET `1548/1548`, Tray `2/2`, Installer `9/9`. Summaries: `.work/test-results/run-tests-20260823T131637592Z-ea6fdf046f5a49788cd4f1c45986a694.summary.json`, `.work/test-results/run-tests-20260823T135242162Z-11b6b5fca41b485e8d055444d58d3276.summary.json`, `.work/test-results/run-tests-20260823T135931124Z-df2a47cc866d471c8a0ca7772da68769.summary.json`.

Classification remains intentionally strict: the owner leak is confirmed and the post-fix evidence strongly supports it as the current flaky-test root; no failure-time direct stack or legal production-equivalent selector was obtained, so Task 2 remains `BLOCKED` for formal reproduction. This report does not claim real desktop acceptance of plug/unplug logging, heartbeat persistence, idle timing, or three-tier power-plan switching.
