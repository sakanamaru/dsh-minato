# The status snapshot dsht reads

The overlay reads one file that an existing plugin already produces. This document describes that
file as it actually exists, verified against a live one - not as a format designed for dsht.

## Who writes it

`dsh-minato-bridge` (aka `shio-bridge`, v0.2.1), an optional read-only dsh plugin installed in the
`desktop` profile. It samples the session store every three seconds.

## Where it is

`%USERPROFILE%\.dsh\shio-bridge\sessions.json`

`DSH_HOME` is honoured when set. `dsht hud --dump` prints the resolved path, so a misconfiguration
is visible rather than silent.

## Shape

```json
{
  "formatVersion": 2,
  "generatedAt": "2026-11-25T11:25:44.964Z",
  "intervalMs": 3000,
  "sessions": [
    {
      "id": "session-3ef4a66f-7bf8-4bb0-b365-64921db64ae7",
      "live": true,
      "active": true,
      "seq": 2908,
      "cwd": "D:\\work\\dsh-minato",
      "createdAt": "2026-11-20T03:50:06.012Z",
      "lastPromptAt": "2026-11-25T11:22:39.594Z",
      "turns": 7,
      "steps": 263,
      "uncachedInputTokens": 3123106,
      "outputTokens": 372075,
      "cacheReadTokens": 127916032,
      "cacheWriteTokens": 0,
      "contextWindow": 1048576,
      "pressureTokens": 671471
    }
  ]
}
```

There is also a `title` field, which this tool deliberately does not read.

## What the overlay derives

| Overlay row | Derivation |
|---|---|
| headline | `sessions.length` and the number with `active: true` |
| dot colour | teal when something is active, grey when idle, amber never (see below) |
| context | `pressureTokens / contextWindow` of the focus session, clamped to 0..1 |
| tokens | the focus session's four buckets summed |
| project | last path segment of the focus session's `cwd` |

The **focus session** is the active one if any, otherwise the one with the newest `lastPromptAt`.
All three rows describe that one session, so the numbers are comparable. An earlier version summed
tokens across every session and produced a meaningless 11-billion figure: nine sessions, some days
old, cache reads included.

## Absent data stays absent

- A missing `contextWindow` or a zero window yields `unknown`, never a zero percentage.
- A missing `cwd` yields `unknown`, never an empty project name.
- A session without an `active` flag makes the dot `unknown` rather than claiming idle.
- An older `generatedAt` than 45 seconds dims the body text and reports the values as stale.

## What the producer cannot supply

There is no pending-question count, so the overlay has no "waiting for you" row. Adding one means
extending the producer, not guessing in the reader.

## A note on privacy

The producer records session **titles**, which are derived from prompts, and writes them to disk in
this file. dsht never reads or displays them, but the file itself contains conversation-derived
text. That is a property of the installed plugin, not of the overlay.
