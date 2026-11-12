# Quasar

[![Verify](https://github.com/Docbuchanan84/Quasar/actions/workflows/verify.yml/badge.svg)](https://github.com/Docbuchanan84/Quasar/actions/workflows/verify.yml) · MIT

Quasar is a simulation-first 4X and grand strategy project inspired by Aurora C#.
The playable System Lab includes a Solar System campaign, automatic course
planning, 2D and 3D maps, and campaign save and recovery controls. See the
[development log](docs/DEVLOG.md) for release history.

## Play the System Lab

New campaigns start with the Sun, eight planets, selected moons and small bodies,
two fictional Earth stations, and Pathfinder. C# advances their 3D positions on
the simulation clock. Select Earth and use **Focus Selected** in either map for
the local view. See the [Solar System guide](docs/SOLAR_SYSTEM.md).

1. Select Pathfinder in either map or the object list. Stop time processing before plotting.
   Every new Pathfinder starts with an exploration torch and 20,000 kg of
   reaction mass for interplanetary flight.
2. Choose **Plot Course**, select any modeled body or station, and choose an arrival
   condition. Quasar searches for the fastest and lowest-maneuver-cost routes
   the installed engine and remaining fuel can execute. Start with an Earth
   approach in a fresh campaign.
3. Use the linked travel-time and propellant-reserve controls to adjust the
   course. Lock either to hold it while Quasar searches the other choices.
   Review the burn schedule, ETA, propellant, and braking reserve. Select **Issue Order** to
   commit that course. **Cancel preview** keeps the existing movement state.
   Gold dashes show previews and green shows
   committed paths. Both views show departure and future arrival markers.
4. Choose 1, 5, or 30 seconds, 1 minute, 1 hour, or 1, 5, 15, or 30 days.
   Select **Once** or **Continuous** and press **Play**. Continuous runs on the
   host even when you lock your phone or close the browser. A ship arrival or
   **Stop** pauses it. The clock displays flight events and their simulation time.

On iPhone, open Quasar from the Home Screen, tap **Enable arrival notifications**,
and allow the permission prompt. Quasar can then alert you when an arrival
pauses the game. Tap the notification to return to Quasar.

Drag or swipe to pan in 2D. In 3D, drag to orbit and use right/middle/Ctrl-drag or
two fingers to pan. Both views support wheel/pinch zoom and **Reset View**.
**Focus Selected** tracks the selected object in either view.

C# owns the simulation clock and movement. Automatic courses refine Lambert
transfer guesses into finite burns connected by analytical coasts, with automatic
gravity transitions during burns and coasting. The reusable
simulation also supports hierarchical celestial queries and moving targets.
The browser exposes one ship. Use **Engine burn** on Pathfinder to command a
prograde or retrograde burn while paused and freely coasting. Course burn
boundaries, timed cutoff, and propellant exhaustion appear as flight notices.
The lab is shared across browsers. The host restores SQLite checkpoints
and exposes **New Game**, **Save Game**, **Named Save**, and **Load Game**.
Choose **Continue** inside Load Game to recover the latest readable save.
See the [campaign workflow](docs/CAMPAIGN_LIFECYCLE.md) for first run,
confirmation, and recovery.

Open **Torch Designer** from the top navigation or Pathfinder’s Propulsion
panel to compare fusion drives in a test burn or fit a validated design to the
campaign ship. **Debug refuel** fills the installed tank while paused and
coasting. See the [Torch Designer guide](docs/TORCH_DESIGNER.md).

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
checks. See [development setup](docs/DEVELOPMENT_SETUP.md) and the disposable
[campaign](docs/CAMPAIGN_LIFECYCLE.md#verification) and
[course planning](docs/COURSE_PLOTTING.md#verification) browser checks.

## Project records

- [Parametric engineering designers](docs/PARAMETRIC_ENGINEERING_DESIGN.md)
  documents the approved engineering and teaching direction behind the
  Fusion Torch Designer.
- [Designer handoff](docs/DESIGNER_HANDOFF.md) covers the playable workflow,
  the first slice’s design decisions, and the questions for the next slice.

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
