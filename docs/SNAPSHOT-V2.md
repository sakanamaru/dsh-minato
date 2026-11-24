# Snapshot v2 contract

The HUD reads one file; a dsh plugin writes it. This document is the contract between the two, so
neither half has to guess.

## Location

`%LOCALAPPDATA%\dsht\snapshot.json`, written atomically (write a temporary file, then replace).

## Shape

```json
{
  "v": 2,
  "generatedAt": "2026-11-25T05:59:12Z",
  "seq": 41,
  "sessions": [
    {
      "id": "a1b2c3d4",
      "project": "dsh-minato",
      "state": "active",
      "ctxUsed": 0.38,
      "ctxRemainTokens": 126900,
      "tokensTotal": 41200,
      "questions": 1,
      "lastTool": "pwsh",
      "updatedAt": "2026-11-25T05:59:10Z"
    }
  ]
}
```

## Rules the reader relies on

- `v` must be `2`; anything else is rejected rather than guessed at.
- Any field may be absent. Absent means unknown, and unknown is shown as `unknown`, never as zero.
- `state` is one of `active`, `parked`, `idle`; an unrecognised value is treated as idle.
- `ctxUsed` is a fraction of the context window, 0 to 1.
- `tokensTotal` is the cumulative provider-recorded total for that session, all four buckets summed.
- `state` may be absent when the bridge cannot observe it; the overlay then shows an unknown dot
  instead of claiming the session is idle.
- `questions` counts pending questions for that session.
- A session without `id` is dropped.

## What must never be written

Session titles, message text, tool arguments and outputs, file names, call ids, tokens, or any
credential. The snapshot is read by a separate process and may be inspected by the user; it carries
counts and states only.