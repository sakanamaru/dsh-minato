# Contributing

## Scope first

`docs/SCOPE.md` is binding. The command surface is capped at eight, and a ninth command has to
replace an existing one. Please open an issue describing the failure you hit before writing code;
this repository is more interested in the failure than in the feature.

## Build and test

```powershell
powershell -File scripts/run-tests.ps1
```

Pull requests should keep the tests green and add at least one assertion that fails if the change
is reverted. A claim without an assertion is not reviewable.

## What gets merged

- A reproducible problem, a small change, and a test that would have caught it.
- Diagnostics and repairs that stay read-only by default and require explicit consent to write.
- Documentation corrections, always welcome.

## What gets declined

- New subcommands without a removal or a strong failure report behind them.
- Dashboards, charts, plugin-market behaviour, theming, telemetry: those live elsewhere or do not
  belong here.
- Rewrites that are not attached to a bug.

## Style

`.editorconfig` is the formatting contract. Comments explain why, not what. User-facing strings
are English, produced by `ReportRenderer`, and carry no decoration. Unimplemented behaviour
returns exit code 3 rather than a silent no-op.
