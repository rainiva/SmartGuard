# Task 4 report: fail-fast process stop and Tray startup diagnostics

## Scope

- `Tests/Integration/SmartGuardStop.ps1`
- `Tests/Integration/TrayCoreUserFlow.Helpers.ps1`
- `Tests/Integration/TrayCoreUserFlow.Tests.ps1`
- `Tests/SmartGuard.Tests.ps1`

## RED

Added a Pester regression test that mocks `Get-Process` as PID `4242`
(`SmartGuard.Engine`) and makes `Stop-Process` throw
`UnauthorizedAccessException`.

The pre-fix targeted run failed as expected:

```text
Expected regular expression '4242' to match 'Access denied', but it did not match.
```

## GREEN

- Stop attempts now retain the exception for every affected SmartGuard PID.
- `Assert-SmartGuardProcessesExited` polls only the target PID and the named
  SmartGuard Engine/Tray processes, with a timeout capped at two seconds.
- Any residual process reports PID, name, current elevation state, retained
  stop errors, and an administrator/full-gate recovery instruction.
- `Wait-StatusFile` checks the tracked engine process on every iteration and
  immediately reports its PID, exit code, status path, and UTF-8 startup-log
  tail if it exits before status publication.

## Verification

Targeted Pester GREEN run:

```text
Tests Passed: 2, Failed: 0, Skipped: 0, Inconclusive: 0, NotRun: 93
```

`git diff --check` completed with no whitespace errors.

An unfiltered `Tests/SmartGuard.Tests.ps1` invocation discovered 95 tests and
showed no failure before its output stream ended after the existing
`BUILD-SUCCESS-OUTPUT` fixture. It did not produce a final `PassThru` result
object, so this report does not treat that unfiltered invocation as a complete
gate result. The targeted GREEN run above is the authoritative result for this
slice.

## Safety boundary

No real Tray integration run was performed. In particular, this task did not
create, stop, or inspect a real elevated SmartGuard process. The added tests
use Pester mocks only.
