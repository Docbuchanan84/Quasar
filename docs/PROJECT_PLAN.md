# Quasar project plan

Date: 2026-11-08. Frontend library roles are approved as recorded in
[DECISIONS.md](DECISIONS.md); other engineering recommendations remain proposals.

## Context and scope

The supplied Task 001 brief is the initialization specification. No existing
source files or commits were present when the directory was inspected.
The user subsequently supplied the **Quasar — Game Design Record** as the primary
design source. Its live link, dated local text snapshot, provenance, and design
status conventions are in [SOURCES.md](SOURCES.md). The initial searches had not
located it; that context gap is now resolved. The owner selected the shared
Google Doc for Quasar knowledge, design, and tracking. Repository plans support
implementation; Obsidian is not used for this project.

Task 001 stopped at tooling and scaffolding. A subsequent authorized hosting
task added the private mobile skeleton and read-only backend health connection.
The simulation, game APIs, time controls, entities, and map views remain future
work. See [HOSTING.md](HOSTING.md).

## Recommended initial architecture

Frequent iPhone browser play is an approved pillar; the owner's iPhone 17 Pro is
the primary phone target. Design core interactions for touch and portrait while
retaining depth and desktop layouts. See [mobile and remote access](MOBILE_AND_REMOTE_ACCESS.md).

Use one .NET server process hosting an independent simulation library, plus one
browser client. Independent means the simulation is runnable and testable without
ASP.NET or a browser; it does not require a separate simulation service or process.

Dependency directions:

```text
Quasar.Server -----------> Quasar.Simulation
Quasar.Simulation.Tests -> Quasar.Simulation
Quasar.Client --HTTP (later)--> Quasar.Server
```

The simulation must not reference the server, browser, HTTP, database frameworks,
or map libraries. The server should translate requests into validated commands
and expose deliberate state snapshots, rather than serializing mutable internal
objects. The client displays snapshots and submits intent; it never owns the
authoritative clock or calculates authoritative outcomes.

Recommended command handling starts with one ordered simulation writer. This
makes ownership and command order understandable before introducing concurrency.
Parallelize independent calculations only after profiling and proving equivalent
results. ASP.NET request concurrency must not become concurrent simulation writes.

Start with domain folders inside the simulation assembly when real code appears.
Do not create dozens of domain projects, a generic entity framework, ECS,
microservices, event sourcing, or a plugin API in advance of concrete needs.

Keep HTTP DTOs in the server initially. Add a contracts assembly only when another
.NET consumer needs it; TypeScript cannot directly share a C# assembly. OpenAPI
client generation is a later option if maintaining transport types becomes costly.
Add persistence adapters and another test project when a save/load requirement
exists. Do not make the simulation depend on a chosen storage mechanism.

## Repository organization

```text
Quasar.slnx
global.json / Directory.Build.props / NuGet.Config
src/
  Quasar.Simulation/       # Empty C# library, no external package dependencies
  Quasar.Server/           # Built-client hosting and read-only health; no game endpoints
  Quasar.Client/           # React/TypeScript/Vite mobile skeleton
tests/
  Quasar.Simulation.Tests/ # xUnit project; behavior tests start with Task 002
docs/
  PROJECT_PLAN.md
  DEVELOPMENT_SETUP.md
  DECISIONS.md
scripts/                  # Setup, verification, and separate start commands
.vscode/                  # Optional editor tasks and extension recommendation
AGENTS.md                 # Local workflow and preservation rules
```

Keep the .NET solution small. Vite is built separately, not through MSBuild.
Add benchmarks, integration tests, shared contracts, or a persistence assembly
only when a milestone provides a useful reason.

## Technology assessment

| Technology / status | Assessment and timing |
| --- | --- |
| C# / .NET 10 | Appropriate for a headless simulation and testable domain logic. Runtime choice alone does not guarantee simulation determinism or performance. |
| ASP.NET Core | Suitable command/query host. Start in-process with the library; a separate service would add deployment and synchronization costs. |
| React — Approved | Controls and dense inspection/data screens; retain TypeScript/Vite. Keep authoritative rules and per-frame rendering work out of React UI state. |
| Babylon.js — Approved | 3D galaxy/system views and globes. Package installed; coordinate transforms, detail management, and performance still need validation. |
| PixiJS — Approved | 2D planetary/operational/tactical maps and plans. Package installed; projection, map interactions, labels, and detail behavior still need implementation. Its ticker must not drive simulation time. |
| MapLibre GL JS — Deferred | Not part of the approved initial frontend stack and not installed. Revisit only if a concrete requirement justifies it. |
| PostgreSQL | Reasonable candidate for query-heavy persistent history. Premature for the clock prototype. Compare snapshot files or SQLite for a single-player save milestone before choosing an operational database. No substitution has been adopted. |
| HTTP / WebSockets | HTTP commands and snapshots are the recommended first connection. Evaluate native WebSockets versus ASP.NET SignalR if push updates become useful; neither is installed as a separate dependency now. |
| Tailscale — Approved | Private skeleton deployed on HTTPS 9443, same-origin built client and health API. Owner confirmed phone access. Existing services preserved; unattended startup and game-command authorization remain pending. |
| Git / GitHub | Local Git is ready. A remote backup and CI are useful later, after the user chooses repository visibility and publication. |

References checked during initialization: [PixiJS architecture](https://pixijs.com/8.x/guides/concepts/architecture),
[MapLibre globe example](https://maplibre.org/maplibre-gl-js/docs/examples/display-a-globe-with-an-atmosphere/).
The recommendations above are engineering judgments, not performance results.
The owner approved the frontend roles on 2026-11-08. See
[frontend architecture](FRONTEND_ARCHITECTURE.md) for the implementation boundaries.

## Main risks to validate early

1. **Time model.** The source calls for seconds-scale activity through roughly
   30-day advances. This cannot mean blindly iterating
   every second across every domain. Define discrete time units, event order,
   interruption rules, and what a long advance means before selecting a scheduler.
2. **Determinism.** Explicit elapsed time, controlled randomness, stable ordering,
   and deterministic command handling are candidates. Define whether reproducible
   results are required only within a build or across platforms/builds. Compiler
   deterministic builds do not prove deterministic simulation behavior.
3. **Scale.** Use representative entity and event counts after correctness exists.
   Measure elapsed time, allocations, memory, and snapshot sizes. Do not claim
   thousands of locations are supported based on an empty solution build.
4. **Browser load.** Dense tables, selection state, and rendering need profiling;
   update display state at a useful cadence rather than shipping the entire
   universe on every simulation step. The first rendering validation should show
   one small shared dataset in Pixi 2D and Babylon 3D with a React inspector;
   measure frame time, memory, label density, selection, and coordinate agreement.
   Include the real iPhone 17 Pro: touch, portrait/landscape, battery/heat,
   background/resume, and Wi-Fi/cellular reconnection. Browsing from a phone must
   be a playable core workflow, not merely a desktop-size view scaled down.
5. **Persistence/history.** Establish atomic saves, recovery behavior, save-version
   migration, and history retention before coupling rules to a database schema.
6. **Operating environment.** This machine has about 16 GiB RAM, runs a Windows
   Insider build, and stores the repo under OneDrive. Keep generated outputs
   ignored by Git; Git ignore does not exclude files from OneDrive syncing.
   If sync conflicts or file locks occur, propose a non-synced checkout before
   moving anything. Future live databases should use an intentional data location.
7. **Scope and learning.** Implement one vertical slice at a time. The long-term
   design is a direction, not a requirement to implement every domain first.

## Milestones

| Milestone | Deliverable and completion check |
| --- | --- |
| 001 — Initialization | Verified local toolchain, minimal scaffold, reproducible commands, and documented open decisions. No game behavior. |
| 002 — Headless clock | Small simulation clock with explicit advancement and focused automated tests. No API or browser work yet. |
| 003 — Host command boundary | One authoritative in-memory simulation, snapshot query, pause/resume and advance commands, ordered command execution, and host integration tests. |
| 004 — Browser connection | Display clock and paused state; submit pause/advance commands over HTTP. Backend independently runs and remains authoritative. A rejected command displays a useful error. |
| 005 — Tiny simulated universe | A few entities with stable identifiers and one simple update rule. Deterministic replay from a known initial state and visible browser results. |
| 006 — Validate before expanding | Measure representative loads, decide time/event scheduling, evaluate persistence and save/reload, and spike map rendering with local assets. |

The first working prototype is complete after 005: authoritative time,
pause/advance controls, a few entities, browser/backend communication, and
automated behavior tests. WebSockets, maps, PostgreSQL, and cloud services are
not prerequisites for that prototype.

## Exact next coding task: Task 002

Implement a minimal `UniverseClock` in `Quasar.Simulation`, with these proposed
acceptance criteria to confirm at the start of that task:

- A new clock starts at elapsed time zero.
- Explicit nonnegative durations advance simulated time; zero is a no-op.
- Negative durations and overflow are rejected without mutating the clock.
- Equivalent advance sequences produce the same elapsed value.
- Focused xUnit tests cover these cases and run with `scripts/Verify.ps1`.
- Use a .NET duration representation initially; document its unit and range.
  Do not introduce a universal calendar or real-time background scheduler yet.

This creates a small, independently testable foundation before pause semantics,
API contracts, entity architecture, or display controls complicate the work.
