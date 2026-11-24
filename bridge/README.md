# dsh-minato-bridge

Read-only status bridge for DeepSeek Harness. Once a second it writes a small JSON file that an
external tool can read:

`%LOCALAPPDATA%\dsht\snapshot.json`

## What it writes

Counts and states only - no message text, no titles, no tool arguments, no credentials:

```json
{
  "v": 2,
  "generatedAt": "2026-11-25T06:40:12Z",
  "seq": 41,
  "sessions": [
    { "id": "a1b2c3d4", "ctxUsed": 0.38, "ctxRemainTokens": 126900, "tokensTotal": 41200 }
  ]
}
```

Fields are omitted when they cannot be established. The reader shows omitted fields as
`unknown`, never as zero.

## Where the numbers come from

| Field | Source |
|---|---|
| `sessions[].id` | `ctx.sessions.list()` |
| `ctxUsed`, `ctxRemainTokens` | the token meter's `contextPressure` projection (`projectedTokens` / `contextWindow`) |
| `tokensTotal` | the token meter's `tokenUsage` projection, four disjoint buckets summed |

The projection read goes through `ctx.sessionProjections.stateOf(session, key)`. That registry-level
call is documented as deprecated in favour of a domain service; the call is isolated in one function
so it can be swapped when that service is exposed. If a projection is unavailable the field is
simply omitted - the bridge never invents a number.

## What it does not do

- It never reads or writes session content.
- It makes no network requests.
- It does not start, stop, repair or reconfigure anything.

## Install

```
dsh plugin add ./bridge
```

Then run `dsht hud` to see the overlay, or `dsht hud --preview out.png` for a still image.
