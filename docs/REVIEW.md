# Review guide

Written for an external reviewer. It states what to check, what is deliberately unfinished, and
how to exercise the destructive path without touching a real installation.

## How to run it safely

```powershell
dotnet build src/Dsht.Toolbox/Dsht.Toolbox.csproj -c Release
dotnet run --project tests/Dsht.Toolbox.Tests -c Release

# Read-only diagnosis of the real install
dotnet run --project src/Dsht.Toolbox -c Release -- diag
dotnet run --project src/Dsht.Toolbox -c Release -- diag --json

# Diagnose a fixture instead of the live install
dotnet run --project src/Dsht.Toolbox -c Release -- diag --dir C:\fixture\install --updater C:\fixture\updater
```

`diag` without `--fix` performs no writes. `--fix` refuses to run destructive steps without
`--yes`, and stops at the first failing step.

## What is worth reviewing

1. **The planner.** `RemedyPlanner.Plan` is the whole decision surface. Check that every
   finding corresponds to a test, and that no branch can report a warning as healthy.
2. **The abort path.** `RemedySession.Run` stops on the first failure; `stop-holders` runs before
   `clear-updater-pending` because of file handle ordering. Both are asserted.
3. **The optimistic-result trap.** The outcome is decided by re-collected facts, not by the
   executor's return value.
4. **Honest unknowns.** Version comparison refuses to order versions it cannot parse, and
   reports `version-same-core` when the installed side only exposes an assembly version.
5. **The surface cap.** `CommandSurface.MaxCommands` is 8 and is asserted; six commands are
   stubs that exit 3 on purpose.

## Known limits, stated plainly

- The repair path has **not** been exercised against a live, broken installation yet. It is
  covered by unit tests through `FakeRecorder`, and the diagnosis half has been run against a
  real machine (10 processes holding the install directory, a stale staging area).
- Killing processes will close a running dsh session, including anything the user is doing.
  That is why it sits behind `--yes`; a future version should prefer asking the app to quit.
- Only the default install locations are probed; `--dir`/`--updater` exist for anything else.
- `hud`, `scan`, `repair`, `backup`, `profile`, `log` are unimplemented.

## Style expectations this code tries to meet

- Comments explain *why*, never restate the next line.
- No dead code and no placeholder markers; unimplemented behaviour exits with an explicit code.
- Every user-visible string is produced by one renderer, in English, without decoration.
- Constants are named; magic numbers in the host layer carry a reason.