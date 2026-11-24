# dsht — dsh toolbox + HUD (v4, in progress)

A single self-contained Windows executable that watches DeepSeek Harness from **outside** it:
an always-on-top, click-through HUD, plus a small set of single-purpose repair tools.

> Why outside: dsh plugins only live inside dsh's own Web UI. An overlay that sits above *any*
> window — and that still works when dsh refuses to start — can only be an external process.

## Status

Early v4 rebuild. `diag` is the first implemented command; the rest are stubs that exit 3.

## Commands (hard cap: 8)

```
dsht diag            diagnose a broken desktop install (read-only)
dsht diag --json     machine-readable facts
dsht diag --fix      repair (stops holders, clears updater pending, re-runs installer)
dsht hud             edge/top-left overlay (not implemented yet)
dsht scan|repair|backup|port|profile|log   (stubs, exit 3)
```

## Build

```powershell
dotnet build src/Dsht.Toolbox/Dsht.Toolbox.csproj -c Release
dotnet run --project tests/Dsht.Toolbox.Tests -c Release
```

## Scope discipline

See [docs/SCOPE.md](docs/SCOPE.md). Short version: at most 8 subcommands, each ≤300 lines of core
logic with a failing assertion, and anything with no usage evidence after 8 weeks gets deleted.

## License

MIT
