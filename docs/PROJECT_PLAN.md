# Quasar implementation plan

## Current milestone

The private System Lab is playable at release `a3a052f`, deployed on 8 November
2026. [PR #7](https://github.com/Docbuchanan84/Quasar/pull/7) consolidated the
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
| Course plotting | Planet and duration selection, shared path and markers, revision-bound preview and commit, and delta-v display |

The [System Lab guide](SYSTEM_LAB.md), [integration record](AGENT_INTEGRATION.md),
and [development log](DEVLOG.md) document contracts, source branches, and test
evidence. The original lab's physical-phone portrait 2D acceptance remains
recorded in [mobile validation](MOBILE_AND_REMOTE_ACCESS.md).

## Next validation

Exercise the current course workflow, arrival, and both maps on the owner's
iPhone 17 Pro and record iOS/browser versions. Cover gestures, landscape,
background/reconnect, and cellular-only access separately.

## Open implementation and design work

- Recoverable versioned save/load with atomic writes, stable IDs, history, and
  stopped-on-load behavior is a proposed development slice. Format, migration,
  retention, and storage choices remain open.
- Propulsion and mass modelling, finite thrust, fuel expenditure, fleets, collision,
  docking, and player-facing moving-target/waypoint selection remain open. The
  headless trajectory planner already supports moving-target interception.
- Permanent scheduling, variable fidelity, look-ahead, cross-platform determinism,
  terrain, and sustained large-world and phone performance require further work.
- Private hosting uses manual startup after reboot. Unattended startup remains open.

The [decision record](DECISIONS.md) tracks approvals and candidates. The shared
Google Game Design Record carries project direction and discussion.
