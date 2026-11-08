# Quasar implementation plan

## Owner-requested prerequisite consolidation (8 November 2026)

Agents 1 (time), 2 (celestial positions), 3 (spacecraft movement) and 5 (navigation)
are combined as described in [AGENT_INTEGRATION.md](AGENT_INTEGRATION.md), retaining
their approved scope and limitations. Agent 4 course plotting follows on this
baseline; it must preserve the same simulation and map contracts.

## Agent 3 — spacecraft trajectories (8 November 2026)

The explicit Agent 3 task supersedes the historical next-task recommendation for
this branch. Implemented on Agent 2: pure Lambert planning, analytical gravity
coasts, explicit SOI-patched itineraries, delta-v, headless execution/events,
cancellation/replanning and shared map samples. Fuel remains unavailable without
propulsion/mass data. See [SPACECRAFT_TRAJECTORIES.md](SPACECRAFT_TRAJECTORIES.md).
No save format, permanent scheduler, automatic SOI route search or dependency was
selected. Recommended next task: define a concrete spacecraft propulsion/mass
model before implementing fuel expenditure or finite thrust.

## Current objective

**Task 002 status: implemented, deployed, automated checks passed, and initial
phone experience accepted by the owner on 8 November 2026.** Remaining device
qualification is tracked below; it does not erase the successful actual-phone
2D check. No Task 003 implementation is authorized by this status update.

Task 002 is the **System Lab** vertical slice requested by the owner on 8 November
2026. It supersedes the earlier clock-only Task 002 and incorporates the previous
003–005 connection/universe milestones plus the approved initial map stack.
The original initialization milestone is complete; the historical sequence remains
in Git history rather than serving as the active task specification.

## Task 002 acceptance

- Headless C# fixture: one star, two planets, one ship; stable identities and XYZ kilometres.
- Explicit deterministic elapsed time, efficient long advances, Once/Continuous Play,
  Stop, and arrival interruption. No permanent scheduling framework.
- Serialized ASP.NET commands and immutable snapshots; revision/instance checks,
  same-origin JSON commands, lease expiry, and no browser authority over game time.
- React inspector/orders/history with shared selection across PixiJS and Babylon.js.
- Touch camera controls, responsive layouts, lazy renderer lifecycle, representative
  planetary surface inset, and clearly labeled provisional mechanics.
- Simulation/session tests, HTTP smoke checks, desktop/mobile browser checks, existing
  full verification, private deployment, shared record update, focused branch and PR.

See [System Lab implementation](SYSTEM_LAB.md) for contracts and operational details.
See [development log](DEVLOG.md) for actual checks and remaining device validation.

## Acceptance evidence and handoff

- Implementation commit: `32d45a8`; [Task 002 PR #2](https://github.com/Docbuchanan84/Quasar/pull/2).
- [Implementation CI passed](https://github.com/Docbuchanan84/Quasar/actions/runs/42517246183),
  alongside 21 local simulation/session tests and HTTP/browser checks.
- The owner reported that the lab "looks so good already and works just how I
  want" and supplied actual phone screenshots of stopped/processing Continuous
  controls, the selected ship, and changing depth in the 2D tactical map.
- [Mobile validation](MOBILE_AND_REMOTE_ACCESS.md) separates visible evidence,
  the owner's broader acceptance, automated coverage, and checks still outstanding.
- A requested post-test reset was performed by restarting only Quasar and was
  verified at elapsed zero, paused, ship idle, four objects, one initialization
  event. No reset endpoint/button or save system was added.

## Time-control expansion — 8 November 2026

The authorized time-control expansion adds 5 seconds, 30 seconds, 5 days, and
15 days to the existing selector. It preserves the System Lab's exact tick clock,
analytic travel, arrival interruption, and Once/Continuous Play/Stop contract.
This focused extension does not select a permanent scheduler or save format.

## Next task

The owner requested a focused map-navigation improvement after the time-control
expansion: remove redundant direction controls, protect selection from drags, and
fix 3D orbit/pan/zoom/pivot behavior with the existing renderers. Implemented scope
and test evidence are in [MAP_NAVIGATION.md](MAP_NAVIGATION.md). Physical iPhone
validation of the changed controls is the immediate follow-up; this does not
authorize a new simulation feature or select a new architecture.

Implement a small versioned save/load feature with atomic writes, recoverable
backups, stable identities, history, and stopped-on-load behavior. Select its exact
format and migration policy in that task; no database has been selected.

## Still open

- N-body/escaping trajectories, moving-target navigation, propulsion, physical
  size/time scales, collision and docking. Analytical bound celestial orbits are
  implemented; the visible travel lab still uses fixed-body definitions.
- Permanent scheduling, variable fidelity, look-ahead, and cross-platform determinism.
- Save schema, history retention, large-world performance budgets, and terrain data.
- Physical-phone 3D, full gesture/order/arrival/lifecycle checks, landscape,
  exact iOS/browser versions, cellular-only access, and measured rendering budgets.
- Unattended host startup. Basic actual-phone 2D use and initial acceptance are confirmed.

Keep the C# library independent of web/rendering/storage packages. Preserve the
approved React/TypeScript/Vite, Babylon.js, PixiJS, ASP.NET, and .NET 10 foundation.
PostgreSQL, WebSockets, additional map libraries, and universal entity frameworks
remain deferred/candidates, not prerequisites for expanding this lab.
