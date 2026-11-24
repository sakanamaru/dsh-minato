# dsht

A single self-contained Windows executable that looks after a DeepSeek Harness install from the
**outside**: it diagnoses the failures that leave the desktop app unable to update or start, and
it will grow a small always-on-top status overlay.

> Unofficial. Not affiliated with DeepSeek. No telemetry, no network calls.

## Why an external tool at all

dsh plugins run inside dsh, which means they are unavailable exactly when they are needed most:
when the app will not start, when an update rolls back, or when the installer cannot replace files
that are still held open. Those failures happen outside the plugin sandbox, so the tool that
explains them has to live outside it too.

The upstream reports this tool targets: [discussion #3959](https://github.com/deepseek-ai/deepseek-harness/discussions/3959)
(the updater replaces `node.exe` before the backend exits) and
[discussion #12988](https://github.com/deepseek-ai/deepseek-harness/discussions/12988)
(`MoveFileExW` fails with Windows error 32, ten retries, then rollback).

## Commands

```
dsht diag [--json] [--dir <path>] [--updater <path>]
    Diagnose the install. Read-only.

dsht diag --fix [--yes]
    Close holders, clear the updater staging area, re-run the staged installer.
    Destructive steps require --yes. The run re-checks afterwards.

dsht port [--port <n>]
    Is the web port listening, and which dsh processes exist.

dsht hud|scan|repair|backup|profile|log
    Reserved. They exit with code 3 today.
```

Exit codes: `0` ok, `1` failed, `2` usage, `3` unsupported.

## What a diagnosis looks like

```
dsht diag
  install : C:\Users\<user>\AppData\Local\Programs\DeepSeek Harness
  version : 0.2.2.0  (electron 44.7.0)
  updater : PendingKnown  pending=0.2.2-rc.5

  [ok] main-exe  Main executable present
  [!!] holders  10 process(es) hold the install directory
           This is the direct cause of the Windows error 32/5 seen when updating; ...
  [!!] updater-pending  Updater has a staged installer
           Staged: deepseek-harness-0.2.2-rc.5-win-x64.exe; same version core as installed ...

  plan:
    1. stop-holders  [needs --yes]  - release file handles on the install directory
    2. clear-updater-pending  - a stale staging area makes every following update fail

  verdict: 2 item(s) to repair (run with --fix)
```

## Build and test

```powershell
dotnet build src/Dsht.Toolbox/Dsht.Toolbox.csproj -c Release
dotnet run --project tests/Dsht.Toolbox.Tests -c Release

# Self-contained single file, no runtime needed on the target machine
dotnet publish src/Dsht.Toolbox/Dsht.Toolbox.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o out
```

## The rules this project runs on

`docs/SCOPE.md` is a contract, not a wish list: at most **eight** subcommands, each with core
logic under 300 lines and at least one assertion that fails if the behaviour disappears, and
anything without usage evidence after eight weeks is deleted.

`docs/ARCHITECTURE.md` explains the shape (pure core, one side-effect interface, one renderer),
and `docs/REVIEW.md` is the reviewer's entry point, including how to exercise the repair path
against a fixture instead of a live install.

## Status

Early v4. `diag` and `port` work; the rest are reserved. The diagnosis path has been run against
a real installation; the repair path is covered by tests but has not yet been applied to a live,
broken install.

## License

MIT
