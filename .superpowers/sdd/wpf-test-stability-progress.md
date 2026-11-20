# SDD Progress — WPF test stability root fix

Branch: main (inline execution explicitly requested by user)
Base before Task 1: 2b6b1713b794b81e823a3a31329df4fc8b1488a3
Task 1: complete (commits 2b6b171..12d6405, spec compliant, quality approved; one report-only Minor resolved)
Task 2: in progress
Task 2 status: blocked at reproduction/attribution gate; uncommitted evidence plumbing retained. Fresh verification: probe build 0 warnings/0 errors; WPF probe Pester 3/3; strict-TRX 1/1. After user-authorized installation of fixed 9.0.661903 diagnostic tools under `.work/diagnostics/dotnet-tools/`, five post-review contamination runs completed with real testhost PIDs, six-entry test-order manifests, nonempty counters/stack/trace artifacts, and all exit 0; each is machine-labeled `ReproductionStatus=NotReproduced`. The reader now rejects nonzero-Passed JSON, revalidates TRX, requires ordered evidence, and bounds cleanup. This is still not a reproduction: no child failure occurred and no failure-time attribution exists; production-equivalent remains explicitly blocked.

Latest verification supersedes the counts above: deterministic `--list-tests` discovery now writes a sorted six-test selection manifest before each run; `RequireReproduction` returns 0 only for complete reproduced failure evidence, 2 for healthy-but-not-reproduced, and 1 for infrastructure/evidence failure; timeout tools share a unified controller handoff deadline. Focused WPF probe Pester is 4/4. A fresh contamination smoke passed with actual testhost PID 24644 and all three diagnostic artifacts. The fresh full gate had Pester 126/126 and .NET 1542/1542, but Tray Integration was BLOCKED by canceled elevated PowerShell and Installer Integration was NOT RUN; `Run-Tests.ps1` exit 1. Task 2 remains blocked and no commit or production scheduling change is authorized.

Final adversarial review: no Critical/Important findings. Reproduced now requires nonzero child exit, non-timeout cleanup, Captured order, failed nonempty TRX, and `paged-queued` output; forged payload and discovery-timeout cleanup regressions have Pester coverage. Latest smoke `20260823T121532573Z-i1-s20260829-1427e9a00ca644b3b872503ced28a32c` passed; explicit `-RequireReproduction` correctly returned nonzero for `NotReproduced`.

Post-install final verification: user-authorized `9.0.661903` `dotnet-counters`, `dotnet-stack`, and `dotnet-trace` remain isolated under `.work/diagnostics/dotnet-tools/`. The final script builds with 0 warnings/0 errors; approved-context focused WPF Pester is 4/4. Fresh seed `20260833` contamination smoke is 6/6 Passed with actual testhost and all three nonempty diagnostic artifacts; `ReproductionStatus=NotReproduced`. Deliberate seed `20260832` one-second timeout now produces a complete bounded Failed/TimedOut receipt with explicit cleanup and capture status. These are plumbing/timeout proofs only: no failure-time attribution, no legal production-equivalent selector, no production scheduling change, and no commit. Task 2 remains BLOCKED.

## Fresh full-gate RED and owner correction (2026-08-23)

The later elevated full gate is the current truth and supersedes the earlier all-green .NET note:

- Summary: `.work/test-results/run-tests-20260823T123758579Z-55e98de61a4a45358ffd3af7af2f432a.summary.json`.
- Pester `126/126` passed; .NET `1541/1542` passed with one failure in `SettingsWindowControllerTests.User_clicks_refresh_button_and_sees_new_log_lines`.
- The TRX records `paged-queued+10006ms`, `active refresh=1`, `started=137`, `pending=18`, `log read hook=False`, and zero log items. Tray and Installer layers are `NOT RUN` after the .NET failure; parent exit `1`.
- A separate long Settings diagnostic run (`.work/diagnostics/settings-suite-20260823T124905458Z-537d542e719d44249e49a4c9187ab7f4/`) passed 546/546 but repeatedly captured `SettingsPlanCatalogCoordinator` load lambdas on ThreadPool workers.

The confirmed data-flow has an unowned `Task.Run` per Settings controller in `SettingsPlanCatalogCoordinator`; its CTS is never passed to synchronous `PowerPlanCatalogProvider.LoadWithRetry`, so `Dispose` cannot stop or observe the work. The catalog loader performs synchronous `powercfg` calls and retry sleeps. The log page path separately uses an outer `Task.Run` around a query API whose three stages also use `Task.Run`. The first item is a concrete ownership leak; the second is a scheduling amplifier. The latter remains a hypothesis until a controlled rerun after ownership cleanup.

Task 2 is no longer “no RED”; it is now **RED reproduced, attribution strong but not failure-time direct**. The original Task 3 decision gate must be rewritten before execution: its proposed test-only files do not own the strongest candidate. Next task is an owner-scoped RED for catalog cancellation, task observation, and late-apply suppression in `SettingsPlanCatalogCoordinator`/`PowerPlanCatalogProvider`; no timeout increase, `SetMinThreads` change, retry, or paging-worker redesign is accepted as a substitute.

## Owner-scoped repair and final verification (2026-08-23)

The owner-scoped RED is GREEN. `SettingsPlanCatalogCoordinator` now owns CTS/generation/task observation and blocks late apply after disposal. `PowerPlanCatalogProvider` now exposes a cancellable async retry path with a shared single-load slot and cache reuse; `PowerCfgProcessRunner` gained only an internal test hook. The WPF fixture no longer raises the process-wide ThreadPool floor. Paging, heartbeat/log persistence, idle detection, and business power-plan switching were not changed here.

Targeted GREEN: coordinator `2/2`, WPF host contract + coordinator `9/9`, provider `11/11`; full Settings without the old floor `549/549`. Post-fix diagnostics `549/549` showed ThreadPool queue maximum `1` and final `0`, with no coordinator/catalog/powercfg frames in six snapshots: `.work/diagnostics/settings-suite-20260823T132507442Z-2250439a43ee473ab86a951ef2fc1e26/diagnostic-summary.json`.

Ten consecutive Settings runs all passed `549/549` with exit `0` and `AllPassed=true`: `.work/diagnostics/settings-stability-20260823T133641057Z-4d7995baee514d68a8aedabc3dbef529/summary.json`.

Three fresh elevated full gates all passed with exit `0` and identical layer results: Pester `126/126`, .NET `1548/1548`, Tray `2/2`, Installer `9/9`. Summaries: `.work/test-results/run-tests-20260823T131637592Z-ea6fdf046f5a49788cd4f1c45986a694.summary.json`, `.work/test-results/run-tests-20260823T135242162Z-11b6b5fca41b485e8d055444d58d3276.summary.json`, `.work/test-results/run-tests-20260823T135931124Z-df2a47cc866d471c8a0ca7772da68769.summary.json`.

Current status is not overstated: the unowned catalog work is confirmed and the post-fix evidence strongly supports it as the current intermittent WPF-test root; Task 2 formal reproduction remains `BLOCKED` because no failure-time direct stack or legal production-equivalent selector exists. This slice is not real desktop acceptance of plug/unplug logging, heartbeat persistence, idle timing, or three-tier switching.
