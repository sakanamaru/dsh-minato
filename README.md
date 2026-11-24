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
    Read-only diagnosis of a desktop install.

dsht port [--port <n>]
    Is the web port listening, and which dsh processes are running.

dsht hud|scan|repair|backup|profile|log
    Reserved. They exit with code 3 today.
```

Exit codes: `0` no problems found, `1` problems found, `2` usage, `3` unsupported.

## What a diagnosis looks like

```
dsht diag
  install : C:\Users\<user>\AppData\Local\Programs\DeepSeek Harness
  version : 0.2.2-rc.5  (electron 44.7.0)
  updater : Pending  staged=0.2.2-rc.5

  [ok] main-exe       Main executable present
  [..] app-running    The app is running (10 process(es))
                      Normal. If an update fails with a file-in-use error, quit the app first,
                      including its tray icon.
                      -> Quit DeepSeek Harness from the tray icon, then retry the update.
  [ok] updater-staged A staged installer is present and not newer than the installed build

  verdict: no problems detected (diagnosis only; start the app to confirm it works)
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

Diagnosis only. `diag` and `port` work; the six other commands are reserved and exit 3.

The repair path was designed, implemented, reviewed, and **withdrawn**: four independent reviews
found that it could delete its own installer, target a sibling installation, and report success
without proving repair. The design and the requirements for a future attempt live in
[docs/REPAIR-DESIGN.md](docs/REPAIR-DESIGN.md). Nothing in this build changes the machine.

## License

MIT