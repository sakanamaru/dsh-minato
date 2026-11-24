# Repair: design only, not shipped

`dsht` in this build is **diagnosis only**. This document records the repair design and the
review findings that blocked it, so the next attempt starts from evidence instead of optimism.

## Why it is not shipped

Four independent reviews of the first repair implementation returned the same verdict: do not
merge. The findings, which are requirements for any future attempt:

1. **The plan destroyed its own installer.** Clearing the staging area ran before the installer,
   and the installer was resolved from that same directory, so the sequence could not succeed.
   Requirement: resolve and preserve the exact package outside the deletion target, run that
   preserved file, and assert with a filesystem-backed fixture that it still exists at exec time.
2. **Directory containment used a prefix match.** A sibling directory such as
   `C:\Apps\DeepSeek Harness Backup` counted as inside `C:\Apps\DeepSeek Harness`, so consent to
   repair one installation could close another. Requirement: compare on directory boundaries,
   and re-verify process identity (path plus creation time) before terminating, because PIDs are
   reused.
3. **Tree termination exceeded the validated set.** `Kill(true)` reached descendants that were
   never planned or disclosed. Requirement: terminate only validated targets, or inventory and
   disclose the wider scope first.
4. **Deletion bypassed consent.** Cleanup actions were not marked destructive, so a clear-only
   plan deleted files with `--fix` but without `--yes`. Requirement: every mutating action needs
   consent, and the gate is checked before the first step runs.
5. **A running application was treated as broken.** Any process inside the install directory
   produced a warning and a shutdown plan, even with no update pending. Requirement: separate
   application health from readiness to replace files.
6. **Health did not prove repair.** Postconditions covered only metadata; a missing runtime or
   damaged archive still reported clean. Requirement: repair-specific postconditions, and
   "cleanup completed, health unverified" instead of success.
7. **The installer command line was unverified and may reproduce the failure.** The flags
   `--updated --force-run` are real electron-updater flags, but the upstream reproduction in
   #12988 shows an ordinary install succeeded where the automatic path failed. Requirement:
   establish a version-specific installer contract and verify an ordinary install mode.
8. **Staged package identity was never established.** One `*.exe` in the staging directory was
   enough to plan execution: no hash, publisher, architecture or metadata binding. The real
   `update-info.json` carries `fileName` and `sha512`, not the fields being parsed.
   Requirement: bind the exact filename, verify the recorded sha512 and product identity, reject
   ambiguous candidates before closing anything.
9. **The target directory was not carried into the installer.** `--dir` changed probing and
   process selection, but the installer chose its own destination. Requirement: carry the
   resolved target through the contract and refuse ambiguous targets.
10. **Failures left the machine in an unreported state.** The first failing step returned without
    a re-check, and a still-running installer was reported as failure while continuing to change
    the machine. Requirement: best-effort re-check after failure, an explicit
    "installer still running" outcome, and no overlapping repairs.

## Shape of the next attempt

```
probe -> plan(intent) -> consent gate -> preserve -> stop -> install -> verify -> report
```

Every arrow needs an assertion, and the destructive half needs a Windows integration fixture:
a disposable install tree, a controlled holder process, and a harmless executable that records
its arguments and working directory. Until that fixture exists and passes, no repair path is
reachable from the command line.

## What this build offers instead

`dsht diag` reports what it can prove, names what it cannot, and gives a human-readable next step
for each finding. That is a smaller promise than a repair, and it is one the code keeps.
