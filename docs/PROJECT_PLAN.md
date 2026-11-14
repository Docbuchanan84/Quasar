# Quasar implementation plan

## Current milestone

The playable System Lab brings together the Solar System starter campaign,
automatic course planning, interactive maps, and persistent campaigns. The
[development log](DEVLOG.md) records when features landed and were deployed.

| Delivered | Scope |
| --- | --- |
| System Lab | Solar System starter, Pathfinder, inspectable XYZ, shared 2D/3D selection, and event history |
| Clock | Nine intervals, host-owned Once/Continuous, Stop, exact arrival pauses, and non-pausing flight notices |
| Arrival alerts | Per-device iPhone Home Screen Web Push opt-in for new arrivals that pause the clock |
| Celestial mechanics | Analytical hierarchical position and velocity queries and sphere-of-influence data |
| Spacecraft movement | Lambert transfers, analytical coasting, impulses, and headless event execution |
| Navigation | Collapsible system object tree, gesture-safe selection, pan/orbit/zoom, Reset View, and Focus Selected in both maps |
| Course plotting | All modeled destinations, automatic gravity transitions, finite burns, approach/orbit arrival, linked time/reserve controls and locks, shared map preview, and revision-bound commit |
| Propulsion 1/4 | Single engine and tank, starting exploration torch, read-only Pathfinder performance, and compatible v3 checkpoints |
| Propulsion 2/4 | Finite burns, fuel depletion notices, and active-burn v4 checkpoints |
| Engineering prototype | [Fusion Torch Designer](TORCH_DESIGNER.md), comparative test burns, and compatible Pathfinder campaign refits |
| Propulsion trial | [Sundiver solar sail](SOLAR_SAIL.md), timed trim and coast comparisons, ballistic Earth flyby, and v8 checkpoints |
| Testing controls | Debug refill of the selected ship’s installed propellant tank |
| Navigation 1/2 | Orbital target states, automatic coast primary changes, and checkpoint v5 |
| Campaigns | SQLite checkpoints, autosave, startup restore, and player-facing New Game, Save Game, Named Save, and Load Game |

The [System Lab guide](SYSTEM_LAB.md), [integration record](AGENT_INTEGRATION.md),
and [development log](DEVLOG.md) document contracts, source branches, and test
evidence. The original lab's physical-phone portrait 2D acceptance remains
recorded in [mobile validation](MOBILE_AND_REMOTE_ACCESS.md).

## Solar System starter

New campaigns use the [Solar System fixture](SOLAR_SYSTEM.md), with analytical
planet and satellite motion, two fictional stations, and a finite-course test
ship. Existing Helios campaigns retain their original world when loaded.

## Next validation

Lock Screen arrival delivery has been confirmed by the owner. Validate the
designer’s controls, test burn and refit flow on the physical iPhone. Keep
explicit coverage for both maps, save/load, reconnect, landscape and cellular
access. Record the iOS and browser versions alongside device evidence.

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

- Extend the first torch prototype toward the approved cross-domain engineering
  direction. Component architecture, reactor and thermal behavior, and a design
  library remain open. See the [design contract](PARAMETRIC_ENGINEERING_DESIGN.md)
  and [designer handoff](DESIGNER_HANDOFF.md).

- Player-facing fleets, collision, docking, and moving-target or waypoint
  selection remain open. The
  headless trajectory planner already supports moving-target interception.
- Permanent scheduling, variable fidelity, look-ahead, cross-platform determinism,
  terrain, and sustained large-world and phone performance require further work.
- Private hosting uses manual startup after reboot. Unattended startup remains open.

The [decision record](DECISIONS.md) tracks approvals and candidates. The shared
Google Game Design Record carries project direction and discussion.
