# Security

## What this tool is

A local, offline Windows executable. It makes no network requests, collects no telemetry, and
writes nothing unless a repair is explicitly requested with `--fix --yes`.

## When it writes

- `diag` without `--fix`: read-only. It reads the install directory, the updater cache, and the
  process table.
- `diag --fix`: closes processes holding the install directory, deletes the updater staging area,
  and may run the already-downloaded installer. It requires `--yes` for the destructive steps and
  stops at the first failure.

## Privileges

An elevated run makes the diagnosis more complete (process paths that are otherwise unreadable)
but is not required for the read-only path. Note that closing processes can end a running session.

## Reporting

Open an issue with the command used and the output of `dsht diag --json`. That output contains
paths and versions, not session content. For a suspected vulnerability, say so in the title and
avoid pasting anything sensitive.
