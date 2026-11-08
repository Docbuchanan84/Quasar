# Quasar implementation plan

## Current objective

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

## Next task

Implement a small versioned save/load feature with atomic writes, recoverable
backups, stable identities, history, and stopped-on-load behavior. Select its exact
format and migration policy in that task; no database has been selected.

## Still open

- Real orbital/propulsion models, physical size/time scales, collision and docking.
- Permanent scheduling, variable fidelity, look-ahead, and cross-platform determinism.
- Save schema, history retention, large-world performance budgets, and terrain data.
- Actual iPhone 17 Pro renderer/touch/lifecycle validation and unattended host startup.

Keep the C# library independent of web/rendering/storage packages. Preserve the
approved React/TypeScript/Vite, Babylon.js, PixiJS, ASP.NET, and .NET 10 foundation.
PostgreSQL, WebSockets, additional map libraries, and universal entity frameworks
remain deferred/candidates, not prerequisites for expanding this lab.
