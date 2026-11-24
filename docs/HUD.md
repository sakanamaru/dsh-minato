# HUD design contract

The overlay answers one question at a glance: is the agent working, and is anything waiting for me.
Everything below is a constraint, not a preference; HudLayout and HudScene enforce it in code and
the tests assert it.

## Content

Four lines, never more:

    [dot] 2 sessions / 1 active
          context  38%
          this hour  12.4k
          waiting  1 question

## Rules

| Rule | Value | Why |
|---|---|---|
| Lines | exactly 4 | a fifth line turns a glance into a read |
| Canvas | 176 x 83 at 100%, scaled by the same factor | the overlay is a pill, not a panel |
| Columns | labels at 28, values at 88 | numbers line up, so the eye can compare them |
| Font sizes | 12 headline, 11 body | no size invented ad hoc |
| Spacing | 4-unit grid: 12 padding, 16 row pitch | prevents the drift that made the old UI ragged |
| Accent | one colour | the dot is the only thing allowed to shout |
| Colour roles | accent, muted, text, warning | no per-element colours |
| Unknown | printed as "unknown" | a missing value is not zero |
| Stale | body text muted after 45 s | stale numbers must look stale |
| Decoration | none: no shadow, no gradient, no border | nothing to look at but the data |

## States

| Dot | State | Meaning |
|---|---|---|
| teal | working | a session is active |
| amber | waiting | a question is pending |
| grey | idle | sessions exist, none active |
| dark grey | unknown | no snapshot, or a snapshot that does not parse |

## Review without a screen

`dsht hud --preview out.png` renders the same scene through the same code the window will use, so
the overlay is reviewable as an image and assertable in a test. The PNG encoder is hand written and
deterministic, which is why the tests can decode the output and check pixels.

## What is not built yet

The always-on-top, click-through window. The preview path exists first on purpose: it is the part
that can be verified, and it is the part a reviewer needs to judge the design.