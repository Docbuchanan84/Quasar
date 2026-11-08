# Quasar decision record

## Approved — celestial mechanics task scope (2026-11-08)

The owner explicitly requested analytical Keplerian celestial mechanics,
simulation-authoritative arbitrary-time position/velocity queries, hierarchical
orbital systems, explicit units, cache invalidation and shared 2D/3D data. No
spacecraft orders/course plotting are authorized by that request.

**Implementation status: implemented and locally verified.** Reversible choices:
immutable catalogue replacement, one-timestamp bounded cache, elliptic two-body
propagation, GM-derived periods and approximate Laplace SOI. Fixed lab definitions
preserve existing travel behavior. This does not approve permanent trajectory,
scheduler, persistence or N-body architecture. See [contract](CELESTIAL_MECHANICS.md).

Date: 2026-11-08. This record distinguishes user requirements, reversible setup
choices made for Task 001, and proposals that have not received user approval.

## Established by the supplied brief

- Official project name: **Quasar**; the existing checkout folder remains Quasar4x.
- Original simulation-first 4X/grand strategy game inspired by Aurora C#.
- The simulation is independent of the browser and authoritative over game state.
- Preserve existing files, use a small initial structure, and keep local
  development independent of cloud services.
- Task 001 is initialization only; do not implement game features or commit to a
  database schema, entity architecture, or large enterprise design.
- The supplied technologies are candidates. Major architectural substitutions
  require user approval.
- System-wide installations or significant workstation changes require approval.

## Additional design source supplied by the user

On 2026-11-08 the user supplied the **Quasar — Game Design Record** as a project
source. See [SOURCES.md](SOURCES.md) for the live document and dated snapshot.
The record explicitly distinguishes confirmed direction, candidate approaches,
and open questions. Adding it does not approve candidate mechanisms or its
proposed implementation sequence.

Its stated directions include genuinely 3D galactic space, continuous planetary
coordinates with altitude/depth, and seconds-scale activity through roughly
30-day advances. Scheduling, spatial implementation, persistence, entity
architecture, and engine foundation remain open. Rendering choices were approved
subsequently as recorded below. A 2D
projection is not a decision to use a 2D universe.

## Approved — project knowledge and tracking (2026-11-08)

The owner explicitly selected the shared Google Game Design Record for Quasar
project knowledge, design, and tracking, and instructed that Obsidian not be used
for this project. The Google Doc and repository `AGENTS.md` were updated to match.
This is a Quasar-specific exception to general Obsidian guidance, not a change to
other projects or global integrations. Repository decisions, plans, code/tests,
and snapshots retain their implementation/reference roles. No game architecture
or candidate mechanic was approved by this workflow choice.

## Approved — frontend stack and roles (2026-11-08)

**Status: Approved.** Evidence: after discussing React for controls/data screens,
Babylon.js for 3D, and PixiJS for 2D maps, the owner instructed: "Lock those choices
in in the documentation" and authorized environment preparation.

| Component | Approved responsibility |
| --- | --- |
| React | Browser UI, tables, inspectors, forms, menus, and management screens |
| Babylon.js | 3D galaxy/system views, planetary globes, and meaningful altitude/depth |
| PixiJS | 2D maps, operational/tactical views, deck plans, and schematic views |

Retain the existing TypeScript/Vite toolchain. All views represent the same C#
simulation and will share entity identity and coordinate transformations;
rendering/interpolation is presentation, not authoritative game logic.

**Implementation status (updated after Task 002):** React controls, Babylon 3D,
and Pixi 2D maps are implemented against one authoritative C# fixture.
`@babylonjs/core` 9.35.0 and `pixi.js` 8.22.0 retain their locked versions.
Headless checks, desktop browser workflows, and initial owner phone 2D use are
verified. See FRONTEND_ARCHITECTURE.md and MOBILE_AND_REMOTE_ACCESS.md for bounds.

**Deferred:** MapLibre GL JS is not part of the approved initial stack; no package
was installed. Database, push transport, physics integration, React renderer
wrappers, and other optional libraries were not approved by this selection.

**Still open beyond the lab:** planetary projection/tiling, large-world precision,
level of detail, data volume, and measured rendering budgets. The lab's XYZ
kilometres, selection IDs, coordinate transforms, and labels are documented,
provisional implementation choices rather than universal spatial commitments.
See [FRONTEND_ARCHITECTURE.md](FRONTEND_ARCHITECTURE.md).

## Approved — first-class iPhone browser play (2026-11-08)

**Status: Approved.** The owner stated the game will frequently be played in an
iPhone browser and explicitly requested this philosophy be made a design pillar.
The owner identified **iPhone 17 Pro** as the primary phone target. Its iOS/browser
version remains unknown; Safari is the recommended initial testing baseline.

Core gameplay/inspection must be usable with touch on a phone. Preserve simulation
depth through responsive focused views, drill-down, and progressive disclosure.
Account for small screens, orientation, safe areas, keyboard/zoom accessibility,
mobile rendering/battery limits, app switching, and unreliable connections. C#
remains authoritative. Real-device validation is required before claiming readiness.

See [mobile and remote access](MOBILE_AND_REMOTE_ACCESS.md). This pillar does not
approve offline simulation, a native app, or a specific disconnected-time policy.

## Approved — private remote access via Tailscale Serve (2026-11-08)

The owner explicitly instructed "Lock in and set up tailsacale" and requested a
working skeleton for phone access. Private HTTPS on port 9443 now proxies to the
published ASP.NET host at loopback 5080, serving built React assets and read-only
`/api/health` under one origin. Existing routes on 443/8443 were preserved.
The owner confirmed "Works perfectly" on the actual iPhone 17 Pro.

The app currently runs in a managed foreground terminal. Automatic background
launch was rejected by approval review; the failed scheduled task was removed.
Unattended startup and future command authorization remain open. No game-time
semantics were changed. See [HOSTING.md](HOSTING.md) for controls and limits.

## Approved — public GitHub, MIT license, and ongoing notes (2026-11-08)

The owner explicitly requested a public codebase with working GitHub access,
good version control, and continuous notes, then selected **MIT**. The repository
is `Docbuchanan84/Quasar`. The initial bootstrap is authorized on `main`; subsequent
work uses focused branches, pull requests, and automated verification.

Keep a concise entry in `docs/DEVLOG.md` with each meaningful implementation
change, plus relevant decision/plan/setup updates. The shared Google Doc remains
the design record. Public code does not change permissions on private documents
or the running Tailscale service. Private access details and full snapshots are
kept locally and ignored by Git. See [CONTRIBUTING.md](../CONTRIBUTING.md).

## Reversible implementation choices used for Task 001

These are setup choices within the requested scope, not additional recorded user
approvals of permanent architecture:

- .NET SDK 10.0.403, with patch roll-forward and previews disabled in `global.json`.
  Installed under `%LOCALAPPDATA%\Quasar\dotnet`; no system SDK was replaced.
- A three-project `.slnx`: simulation library, ASP.NET host, and simulation tests.
  Server/tests reference the simulation. The library has no external packages.
- React/TypeScript/Vite client based on the official React TypeScript template.
  npm was retained because it was already available; no extra package manager.
- Use the existing Node 24 toolchain; record 24.13.0 in `.node-version` and constrain
  supported Node/npm majors in the client package manifest. This file does not
  install or switch Node automatically.
- Lock npm and NuGet dependency graphs; use locked restore in setup/verification.
- Keep the .NET template's xUnit v2 runner stack for now; no coverage framework
  or permanent placeholder tests. Its actual execution is verified separately.
- Loopback-only development listeners, separate client/server commands, and
  VS Code task definitions. No firewall, certificate trust, or Tailscale changes.
- Use the existing Git branch/configuration. No commit, remote, repository
  publication, or push was performed during initialization.

## Historical Task 001 recommendations

The table records the earlier recommendations, not a claim that Task 002 still
lacks an in-process writer or HTTP boundary. The approved System Lab scope below
authorized the minimal implementation choices that now exist. Broader permanent
architecture choices remain open unless explicitly approved.

| Proposal | Reason / tradeoff |
| --- | --- |
| One host process with a headless simulation library | Simple to debug; a separate simulation service is not needed to achieve independence from the UI. |
| Ordered commands and one simulation writer initially | Predictable ownership and reproducibility; parallel work can follow measured need. |
| Domain folders within one simulation assembly initially | Avoid premature interfaces and package boundaries. Split assemblies when actual dependencies justify it. |
| Explicit simulated time and controlled randomness | Enables repeatable tests; calendar, precision, and replay guarantees need definition. |
| HTTP commands/snapshots before push transport | Fewer moving pieces for the first prototype; evaluate WebSockets or SignalR later. |
| Defer persistence and additional map dependencies | Approved renderer packages are prepared; storage and further libraries still require concrete needs and validation. |
| VS Code and .NET CLI as the initial working environment | Already installed and usable by the agent; Visual Studio Community remains a user option. |

No unapproved major replacement of the proposed stack has been made. Mentioning
SQLite, snapshots, or SignalR is an evaluation proposal only. The frontend
renderers above are now approved.

## Questions requiring investigation

- The Game Design Record is now available via [SOURCES.md](SOURCES.md). Reconcile
  future proposals with its stated directions and preserve its open questions.
- What calendar, numerical precision, and long-advance interruption semantics
  implement the seconds-to-roughly-30-day direction? How should pause interact
  with explicit advance commands?
- What spatial representation, projections, and detail management should the
  approved Babylon.js/PixiJS views use for continuous, multi-layer environments?
- How strong must replay determinism be across machines, versions, and platforms?
- What representative universe sizes and acceptable turn times should guide measurements?
- Is the initial target strictly single-player? Who, if anyone, should access it remotely?
- What do persistent history and player saves need to retain, and for how long?
- How should approved views implement arbitrary spherical bodies, custom terrain,
  local assets, and non-Earth units? MapLibre remains deferred.
- Public GitHub visibility and MIT licensing are now approved; no visibility or
  license decision remains pending.

## How to add future decisions

For each consequential decision, record date, status (proposed / approved /
superseded), user approval or other evidence, context, choice, and consequences.
Do not infer design approval from a scaffold or promote exploratory notes into
requirements. Update this record when an actual choice is made.

## Approved — Task 002 System Lab scope (2026-11-08)

The owner requested the playable System Lab and instructed implementation of its
reviewed plan. This replaces the earlier clock-only Task 002 scope. Confirmed
controls: choose an interval, choose Once or Continuous, press Play; stop manually
or at an event requiring attention. Arrival supplies the first interrupt. The
owner selected in-memory state and a small representative surface inset.

Implementation choices for this lab: fixed fixture IDs/XYZ kilometres, stationary
planets, constant-speed straight-line travel, checked elapsed ticks, one serialized
in-process writer, HTTP snapshots/commands, and a short browser control lease.
These are provisional mechanics, not permanent physics/scheduler/save decisions.
See SYSTEM_LAB.md. Other proposals above are historical unless superseded here;
databases, push transport, universal entity frameworks, and unattended startup
remain open/deferred. Save/load is the recommended next task, not implemented.

## Approved — initial System Lab experience baseline (2026-11-08)

**Status: Approved for the initial Task 002 experience.** After using the prototype,
the owner said: "It looks so good already and works just how I want." Two supplied
portrait phone screenshots show the 2D tactical display, selected spacecraft,
changing depth, and stopped/processing Continuous controls. This provides real
device evidence and owner acceptance of the current visual/control direction.

Preserve the restrained technical display and compact interval selector with
Once/Continuous and Play/Stop as the baseline when extending the lab. This is acceptance
of the initial experience, not approval of a permanent scheduler, travel physics,
save format, or every future UI detail. Further features still need a concrete task.

The screenshots do not establish physical-phone 3D support, exact iOS/browser
versions, every gesture, landscape/lifecycle/cellular behavior, or measured scale.
See MOBILE_AND_REMOTE_ACCESS.md for explicit evidence and remaining checks.
