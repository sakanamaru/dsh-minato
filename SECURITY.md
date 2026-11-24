# Security

## What this tool is

A local, offline Windows executable. It makes no network requests, collects no telemetry, and
**never modifies the machine**: this build is diagnosis only, and the six reserved commands exit
with code 3 instead of doing anything.

## What it reads

- the install directory (`version`, main executable metadata, the `resources/app.asar` header);
- the updater cache (`installer.exe`, `pending\*.exe`, `pending\update-info.json`);
- the process table, filtered to processes whose executable lives inside the install directory.

## Privileges

Read-only diagnosis works unelevated but reports less: paths that cannot be opened are reported
as unreadable, never as absent. An elevated run simply sees more.

## Reporting

Open an issue with the command used and the output of `dsht diag --json`. That output contains
paths, versions and finding codes, not session content. For a suspected vulnerability, say so in
the title and avoid pasting anything sensitive.
