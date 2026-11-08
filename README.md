# Quasar

[![Verify](https://github.com/Docbuchanan84/Quasar/actions/workflows/verify.yml/badge.svg)](https://github.com/Docbuchanan84/Quasar/actions/workflows/verify.yml) · MIT

Quasar is a simulation-first 4X and grand strategy project inspired by Aurora C#.
The playable System Lab runs in the owner's private browser preview. The current
gameplay release is `a3a052f`, with [the integrated baseline](https://github.com/Docbuchanan84/Quasar/pull/7)
and [course plotting](https://github.com/Docbuchanan84/Quasar/pull/8) merged into main.

## Play the System Lab

Helios, Iris, Nereid, and Pathfinder share a right-handed XYZ coordinate frame.
The fixture planets are fixed. Pathfinder starts coasting in Helios gravity.
PixiJS shows the tactical XY projection and Babylon.js shows the same scene in
3D. The planet inspector includes an illustrative surface inset.

1. Select Pathfinder in either map or the object list. Stop time processing before plotting.
2. Choose **Plot Course**, select Iris or Nereid, and review the trajectory. Adjust
   the flight duration or recalculate as needed. The panel shows departure, ETA,
   travel time, approximate path length, delta-v, and feasibility warnings.
3. Select **Issue Order** to commit the reviewed course. **Cancel preview** keeps
   the existing movement state. Gold dashes show previews and green shows
   committed paths. Both views show departure and future arrival markers.
4. Choose 1, 5, or 30 seconds, 1 minute, 1 hour, or 1, 5, 15, or 30 days.
   Select **Once** or **Continuous** and press **Play**. Arrival interrupts
   processing at the exact event time. **Stop** pauses time processing.

Drag or swipe to pan in 2D. In 3D, drag to orbit and use right/middle/Ctrl-drag or
two fingers to pan. Both views support wheel/pinch zoom and **Reset View**.
**Focus Selected** in 3D tracks the selected object.

C# owns the simulation clock and movement. The lab uses gravitational Lambert
transfers, impulsive departure/arrival, and analytical coasts. The reusable
simulation also supports hierarchical celestial queries, moving targets, and
explicit sphere-of-influence patches. The browser currently exposes one ship
and fixed planet destinations. Fuel estimates await propulsion and mass data.
The lab is shared across browsers. Refresh retains state, and a host restart
resets it.

## Run and verify

Use PowerShell 7:

```powershell
.\scripts\Setup.ps1
.\scripts\Verify.ps1
# Separate foreground terminals:
.\scripts\Start-Server.ps1 # loopback 5080
.\scripts\Start-Client.ps1 # loopback 5173
```

Port 5080 must be free before starting the development server. The scripts select
the pinned user-local .NET SDK. Verification covers xUnit, Node navigation and
coordinate checks, formatting, lint, TypeScript, and renderer package/bundle
checks. The current release passed 99 xUnit, 11 Node, and 60 Chromium workflow
and navigation checks. See [development setup](docs/DEVELOPMENT_SETUP.md) and
[release verification](docs/COURSE_PLOTTING.md).

## Project records

- [System Lab](docs/SYSTEM_LAB.md) covers the clock, movement, and HTTP contract.
- [Course plotting](docs/COURSE_PLOTTING.md) and [map navigation](docs/MAP_NAVIGATION.md)
  cover controls and verification.
- [Celestial mechanics](docs/CELESTIAL_MECHANICS.md) and
  [spacecraft trajectories](docs/SPACECRAFT_TRAJECTORIES.md) cover the reusable simulation.
- [Hosting](docs/HOSTING.md) covers private Tailscale publishing and rollback.
  [Mobile validation](docs/MOBILE_AND_REMOTE_ACCESS.md) records initial physical-phone
  2D acceptance and outstanding checks for the current release.
- [Decisions](docs/DECISIONS.md), [project plan](docs/PROJECT_PLAN.md), and
  [development log](docs/DEVLOG.md) separate approved choices, shipped features,
  and open work. The live Google Game Design Record is the shared design source.
  Access details are documented in [sources](docs/SOURCES.md).

Public code: [Docbuchanan84/Quasar](https://github.com/Docbuchanan84/Quasar).
See [contributing](CONTRIBUTING.md) for the branch and CI workflow.
