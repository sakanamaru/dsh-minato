# HUD design contract

The overlay answers one question at a glance: is the agent working, how full is the context, and
which project is it in. Everything below is a constraint, not a preference; `HudLayout` and
`HudScene` enforce it in code and the tests assert it.

## Content

Four lines, never more, all describing one session:

    [dot] 9 sessions / 1 active
          context  33%
          tokens   323.1M
          project  dsh-minato

## Rules

| Rule | Value | Why |
|---|---|---|
| Lines | exactly 4 | a fifth line turns a glance into a read |
| Font sizes | 12 headline, 11 body | no size invented ad hoc |
| Spacing | 4-unit grid: 12 padding, 16 row pitch | prevents the drift that made the old UI ragged |
| Canvas | 176 x 83 at 100%, scaled by the same factor | the overlay is a pill, not a panel |
| Columns | labels at 28, values at 88 | numbers line up, so the eye can compare them |
| Accent | one colour | the dot is the only thing allowed to shout |
| Unknown | printed as "unknown" | a missing value is not zero |
| Stale | body text muted after 45 s | stale numbers must look stale |
| Decoration | none: no shadow, no gradient, no border | nothing to look at but the data |

## States

| Dot | State | Meaning |
|---|---|---|
| teal | working | a session reports `active: true` |
| grey | idle | sessions exist, none active |
| dark grey | unknown | no snapshot, an unsupported format, or no active flag to read |
| amber | waiting | reserved: the snapshot has no pending-question data yet |

## Review without a screen

`dsht hud --preview out.png` renders the same scene through the same code the window uses, so the
overlay is reviewable as an image and assertable in a test. The PNG encoder is hand written and
deterministic, which is why the tests can decode the output and check pixels and chunk checksums.

`dsht hud --dump` prints the resolved snapshot path, staleness, state and the four lines. It is how
the data path is verified where no visible desktop exists.

## The window

A layered Win32 window (WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
always topmost). It never takes focus, never appears in the taskbar, and clicks pass through it to
whatever is underneath. Frames are pushed with UpdateLayeredWindow, so there is no paint loop and
nothing flickers. Ctrl+C stops it; an overlay must not be a process the user has to hunt down.

Two properties of that API bite if forgotten, and both were bugs here first:

- the destination point of UpdateLayeredWindow is also the window position, so a frame update moves
  the window unless the intended origin is passed;
- coordinates are virtualised unless the process declares DPI awareness, which put the overlay in
  the wrong place and would have double-scaled the glyphs at 150 percent.

Verify the window contract with `dsht hud --selftest`: existence, visibility, layered,
click-through, no-activate, no-taskbar, placement and size. That is what a screenshot cannot prove.

## What a screenshot cannot prove here

`--capture` writes an image of the overlay, but only from an interactive desktop. When the process
runs in a session with no visible desktop the screen is a uniform colour, and the command says so
and exits non-zero instead of writing a meaningless white PNG. Appearance therefore still needs a
human on a real desktop; `--preview` renders the identical scene without needing one.
