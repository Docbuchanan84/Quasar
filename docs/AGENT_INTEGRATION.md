# Quasar integration record

## 11 November 2026 — Phone release consolidation

The integration starts from the exact privately played runtime and retains
the fleet benchmark ([PR #40](https://github.com/Docbuchanan84/Quasar/pull/40)),
arrival push and arrival-only pauses ([PR #42](https://github.com/Docbuchanan84/Quasar/pull/42)),
debug refill ([PR #43](https://github.com/Docbuchanan84/Quasar/pull/43)), and
Torch Designer with shared navigation ([PR #45](https://github.com/Docbuchanan84/Quasar/pull/45)).
It incorporates the approved engineering direction
([PR #39](https://github.com/Docbuchanan84/Quasar/pull/39)) and historical
release notes. The [development log](DEVLOG.md) and
[designer handoff](DESIGNER_HANDOFF.md) describe the current release.

## 8 November 2026 — Agents 1–5

The owner explicitly requested coherent merging of Agents 1–5 and authorized
adjustments for consistency. This prerequisite consolidation contains:

| Source | Tip | Role |
| --- | --- | --- |
| System Lab baseline | `8c1a722` | Shared foundation for every task |
| Agent 2 | `2f79207` | Authoritative celestial positions and shared coordinates |
| Agent 3 | `afe1ca4` | Planning, gravitational movement, timestamped display samples |
| Agent 1 | `c0353a0` | All nine time intervals and exact clock/interrupt controls |
| Agent 5 | `a8190fe` | Gesture-safe map navigation, Z-up camera and focus |

Agent 2 precedes Agent 3; Agent 1 is independent, and Agent 5 builds on its controls.
[PR #7](https://github.com/Docbuchanan84/Quasar/pull/7) merged the consolidation
as `fa16262`. The earlier PRs were closed as superseded. Agent 4 followed in
[PR #8](https://github.com/Docbuchanan84/Quasar/pull/8), merged as `a3a052f` and
deployed to the private preview.

Merge resolutions keep one authoritative C# world, the existing host command gate,
shared map scale and reference geometry, Agent 3 display samples, Agent 5 camera/
selection fixes, all nine intervals and both frontend test commands. Interval
tests and the HTTP smoke check now compare against gravitational coasting rather
than assuming an unordered ship is stationary or has status `Idle`.

Validation: `scripts/Verify.ps1` (94 xUnit cases, nine navigation and two coordinate
Node cases, locked build/format, lint, renderer load/bundle and production build),
plus isolated celestial/trajectory HTTP checks and the all-interval HTTP smoke
script. The existing lazy Babylon bundle-size warning remains. Individual source
branches' recorded performance/device evidence retains its original limitations.

[Course plotting](COURSE_PLOTTING.md) records verification on the combined
baseline. The shared Game Design Record now includes the combined release.
