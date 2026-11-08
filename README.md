# Quasar

[![Verify](https://github.com/Docbuchanan84/Quasar/actions/workflows/verify.yml/badge.svg)](https://github.com/Docbuchanan84/Quasar/actions/workflows/verify.yml) · MIT

Quasar is an original, simulation-first 4X/grand strategy project inspired by Aurora C#.
Its authoritative C# simulation runs independently of its browser interface.
Frequent iPhone browser play is a core pillar, alongside desktop use.

## System Lab — Task 002

**Implemented, deployed, and owner-accepted on 8 November 2026.** The owner
confirmed that it looks and works as intended and supplied actual phone
screenshots of the portrait 2D display, selected spacecraft, movement, and
Continuous/Stop controls. See [device evidence and remaining checks](docs/MOBILE_AND_REMOTE_ACCESS.md).

The first playable lab contains Helios, two stationary planets (Iris and Nereid),
and the spacecraft Pathfinder in genuine XYZ space. PixiJS provides a tactical XY
projection; Babylon.js provides a rotatable 3D view of the same C# state.
React supplies selection, inspection, orders, time controls, and event history.

1. Select Pathfinder on the map or in the object list.
2. Choose **Travel to Iris** or **Travel to Nereid**.
3. Select an advance interval: 1, 5, or 30 seconds; 1 minute; 1 hour; or
   1, 5, 15, or 30 days.
4. Choose **Once** or **Continuous**, then press **Play**.
5. Inspect movement, switch maps, or press **Stop**. Arrival interrupts processing
   at its exact simulated timestamp and records an event.

Continuous processing handles up to four selected intervals per wall-clock second.
It stops if the initiating browser stops renewing its five-second lease. Stop
works from another connected browser too. Stop processing before changing controls
or issuing/replacing orders. Each advance uses the full selected duration; an
arrival discards the unused remainder and waits for another Play command.

Map gestures: 2D drag/swipe to pan and wheel/pinch to zoom; 3D drag to orbit,
right/middle/Ctrl-drag or two fingers to pan, wheel/pinch to zoom. Reset View,
compact zoom buttons and 3D Focus Selected remain; redundant direction buttons
are removed. See [navigation controls and checks](docs/MAP_NAVIGATION.md). The
planet inspector includes an illustrative terrain/grid inset, not simulated geography.

**Provisional:** constant-speed straight-line travel at 25 km/s; stationary bodies;
reference orbit guides; enlarged symbols. No gravity, acceleration, collision,
procedural generation, persistent saves, or permanent scheduling architecture.
State survives refresh but resets on host restart. No extra runtime packages were added.

## Run and verify

Use PowerShell 7:

```powershell
.\scripts\Setup.ps1
.\scripts\Verify.ps1
# Separate foreground terminals:
.\scripts\Start-Server.ps1 # loopback 5080; do not run alongside published host
.\scripts\Start-Client.ps1 # loopback 5173
```

The scripts select the pinned user-local .NET 10 SDK without changing system PATH.
Verification runs simulation/session tests, .NET format, lint, TypeScript/build,
and renderer package checks. See [development setup](docs/DEVELOPMENT_SETUP.md).
For an isolated running test instance, `scripts/Test-SystemLabApi.ps1` checks the
HTTP boundary and advances its clock; its default address is loopback 5090.

## Hosting and project records

The existing private Tailscale HTTPS endpoint serves the built app and HTTP API
from one ASP.NET origin. The owner's address remains in ignored local context.
See [hosting](docs/HOSTING.md) for publish/restart/rollback instructions. The PC
must stay awake and the foreground host must remain running; no unattended startup
was added. Basic actual-phone 2D use is confirmed; physical-phone 3D, lifecycle,
cellular-only access, exact iOS/browser versions, and measured capacity remain open.

Task 002 passed 21 automated tests, full local verification, HTTP/browser smoke
checks, and [GitHub CI](https://github.com/Docbuchanan84/Quasar/actions/runs/42517246183).
[PR #2](https://github.com/Docbuchanan84/Quasar/pull/2) tracks the implementation
and follow-up documentation; its current merge/check status is visible on GitHub.
The recommended next development task is recoverable versioned save/load.

The shared Google Game Design Record is the project knowledge/design source;
Quasar does not use Obsidian. See [sources](docs/SOURCES.md),
[decisions](docs/DECISIONS.md), [project plan](docs/PROJECT_PLAN.md), and
[development log](docs/DEVLOG.md). True 3D space and continuous planetary coordinates
remain project directions; the lab's simplified physics do not limit future simulation depth.

Public code: [Docbuchanan84/Quasar](https://github.com/Docbuchanan84/Quasar).
Use focused branches and PRs with passing CI; see [contributing](CONTRIBUTING.md).
