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

## The window

A layered Win32 window (WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
always topmost). It never takes focus, never appears in the taskbar, and clicks pass through it to
whatever is underneath. Frames are pushed with UpdateLayeredWindow, so there is no paint loop and
nothing flickers.

Two properties of that API bite if forgotten, and both were bugs here first:

- the destination point of UpdateLayeredWindow is also the window position, so a frame update
  moves the window unless the intended origin is passed;
- coordinates are virtualised unless the process declares DPI awareness, which put the overlay in
  the wrong place and would have double-scaled the glyphs on a 150 percent display.

Verify the window contract with `dsht hud --selftest`: it checks existence, visibility, layered,
click-through, no-activate, no-taskbar, placement and size. That is what a screenshot cannot prove.

## What a screenshot cannot prove here

`--capture` writes an image of the overlay, but only from an interactive desktop. When the process
runs in a session with no visible desktop the screen is a uniform colour, and the command says so
and exits non-zero instead of writing a meaningless white PNG. Appearance therefore still needs a
human on a real desktop; `--preview` renders the identical scene without needing one.