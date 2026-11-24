# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow semver.

## [Unreleased]

### Added

- `dsht diag`: diagnoses a DeepSeek Harness desktop install that will not update or start.
  Reports install presence, processes holding the install directory (the direct cause of
  Windows error 32/5), the updater staging area, and version skew; plans an ordered repair.
- `dsht diag --fix`: closes holders, clears the staging area, and re-runs the staged installer
  from `%TEMP%`. Destructive steps require `--yes`; the run re-checks afterwards and reports
  what is still wrong instead of assuming success.
- `dsht port`: reports whether the web port is listening and which dsh processes exist.
- Six further commands (`hud`, `scan`, `repair`, `backup`, `profile`, `log`) are reserved and
  exit with code 3; the surface is capped at eight by design.

### Notes

- Dry by default: without `--fix` the tool only reads. `--dir` and `--updater` exist so a run
  can be pointed at a fixture instead of a live installation.

## [0.1.0] - not released

Initial rebuild of the v4 line. The v3 tree (dashboard, overlay, backup chain) lives at
`dsh-minato-legacy` and is frozen at v3.3.0.
