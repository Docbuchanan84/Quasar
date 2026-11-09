# Quasar implementation plan

## Current milestone

The private System Lab is playable at campaign release `f86ffd2`, deployed on
8 November 2026. [PR #7](https://github.com/Docbuchanan84/Quasar/pull/7) consolidated the
initial lab with time controls, celestial queries, spacecraft movement, and map
navigation. [PR #8](https://github.com/Docbuchanan84/Quasar/pull/8) added reviewed
course planning and shared map overlays.

| Delivered | Scope |
| --- | --- |
| System Lab | Helios, two fixed planets, Pathfinder, inspectable XYZ, shared 2D/3D selection, and event history |
| Clock | Nine intervals, Once/Continuous, Stop, exact event interruption, and browser lease |
| Celestial mechanics | Analytical hierarchical position and velocity queries and sphere-of-influence data |
| Spacecraft movement | Lambert transfers, analytical coasting, impulses, and headless event execution |
| Navigation | Gesture-safe selection, pan/orbit/zoom, Reset View, and 3D Focus Selected |
| Course plotting | Planet and moon destinations, automatic fastest and lowest maneuver-cost routes, shared map preview, and revision-bound commit |

The [System Lab guide](SYSTEM_LAB.md), [integration record](AGENT_INTEGRATION.md),
and [development log](DEVLOG.md) document contracts, source branches, and test
evidence. The original lab's physical-phone portrait 2D acceptance remains
recorded in [mobile validation](MOBILE_AND_REMOTE_ACCESS.md).

## Solar System starter

New campaigns use the [Solar System fixture](SOLAR_SYSTEM.md), with analytical
planet and satellite motion, two fictional stations, and automatic single-flyby
course tests. Helios campaigns remain loadable. Propulsion is the next requested
development task after the Solar System preview is verified on the physical phone.

## Next validation

Exercise the current course workflow, arrival, and both maps on the owner's
iPhone 17 Pro and record iOS/browser versions. Cover gestures, landscape,
background/reconnect, and cellular-only access separately.

## Persistence sequence

- [Issue #10](https://github.com/Docbuchanan84/Quasar/issues/10) implements the
  version 1 headless simulation checkpoint contract and detached restore.
  See [simulation state](SIMULATION_STATE.md) for the API and verification.
- [Issue #11](https://github.com/Docbuchanan84/Quasar/issues/11) implements SQLite
  campaign storage, named and rotating checkpoints, schema upgrades, and
  verified backups. See the [storage contract](SQLITE_PERSISTENCE.md).
- [Issue #12](https://github.com/Docbuchanan84/Quasar/issues/12) integrates
  startup recovery, autosave, and campaign operations with the System Lab.
  See the [HTTP and lifecycle contract](CAMPAIGN_LIFECYCLE.md).
- [Issue #13](https://github.com/Docbuchanan84/Quasar/issues/13) adds the
  player-facing New Game, Continue, Save, and Load workflow. Chromium and
  disposable-host regressions cover restart and recovery. Physical iPhone
  campaign validation remains pending.

## Open implementation and design work

- Propulsion and mass modelling, finite thrust, fuel expenditure, fleets, collision,
  docking, and player-facing moving-target/waypoint selection remain open. The
  headless trajectory planner already supports moving-target interception.
- Permanent scheduling, variable fidelity, look-ahead, cross-platform determinism,
  terrain, and sustained large-world and phone performance require further work.
- Private hosting uses manual startup after reboot. Unattended startup remains open.

The [decision record](DECISIONS.md) tracks approvals and candidates. The shared
Google Game Design Record carries project direction and discussion.
