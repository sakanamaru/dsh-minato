# Architecture

## Shape

```
src/Dsht.Toolbox.Core/     pure logic: facts, planning, rendering, orchestration
src/Dsht.Toolbox/          host: command dispatch, Windows probes, Windows executor
tests/Dsht.Toolbox.Tests/  dependency-free assertion runner
```

Three rules keep the code reviewable:

1. **Core never touches the machine.** No `File`, no `Process`, no `DateTime.Now`. Facts come
   in as data (`DiagFacts`), decisions come out as data (`DiagPlan`), and both are trivially
   constructible in a test.
2. **Side effects sit behind one interface** (`IRemedyExecutor`). The real implementation is
   `WindowsRemedyExecutor`; tests use `FakeRecorder` and assert the order, the abort-on-failure
   behaviour, and that the outcome follows a re-check rather than optimism.
3. **One place formats output.** `ReportRenderer` owns both the human text and the JSON, so the
   command layer never composes strings and the two formats cannot drift.

## The diagnosis pipeline

```
DesktopProbe.Collect()      reads the machine, records failures as facts
      -> DiagFacts
RemedyPlanner.Plan()        pure: facts -> findings + ordered actions
      -> DiagPlan
ReportRenderer.Human/Json() renders the plan
      -> stdout
RemedySession.Run()         executes the plan through IRemedyExecutor
      -> re-collects facts -> outcome
```

Every stage is replaceable in isolation, which is why the tests can cover the destructive path
without closing a process or touching a real installation.

## Why the repair is shaped this way

The upstream failure (discussions #3959, #12988) is a Windows sharing violation: the updater
tries to rename the install directory while the app — including its bundled Node backend — still
holds files open. Three consequences shape the design:

- **Close before clearing.** The staging directory cannot be deleted while the backend holds it,
  so the planner emits `stop-holders` before `clear-updater-pending`, and a test asserts it.
- **Never run the installer with the install directory as its working directory.** It renames
  its own working directory during the update, which fails by construction. The executor always
  uses `%TEMP%` and passes `--updated --force-run`.
- **Prove the result.** The session re-collects facts after the plan and reports the new state.
  A step that returned success but changed nothing is reported as a failure.

## Deliberate limits

- Windows only for now. Core is portable and CI builds it on Linux, but the host layer uses
  Windows process and file semantics.
- JSONL-era assumptions are avoided: `diag` reads install metadata only, not session logs, so a
  change of session storage backend does not invalidate it.
- No telemetry, no network calls.
