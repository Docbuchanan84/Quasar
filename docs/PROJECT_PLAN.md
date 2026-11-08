# Quasar implementation plan

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

## Next task

The separately requested **Agent 2 celestial mechanics/position service** is
implemented and locally verified: analytical elliptic hierarchy queries, velocity,
period/SOI, bounded caching and shared map transforms. The fixed-body travel lab
remains. See [contract and limits](CELESTIAL_MECHANICS.md). This explicit owner
request extends the historical Task 002 baseline without selecting a permanent
scheduler, save format or spacecraft navigation model.

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
