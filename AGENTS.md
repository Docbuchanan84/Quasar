# Quasar agent workflow

## Mission and implementation baseline

- **Quasar** is an original, simulation-first, browser-based 4X/grand strategy game inspired by Aurora 4X. The player manages a civilization across space, planets, economics, logistics, engineering, military operations, politics, and characters. The goal is inspectable state, meaningful controls at different levels of detail, persistent history, and flexible time advancement.
- The current foundation (Task 001, 8 November 2026) consists of a C#/.NET 10 simulation library, ASP.NET Core host, React + TypeScript + Vite browser client, xUnit tests project, Git repository, and local development scripts. This is a **historical baseline**; inspect code and history for current status. No game mechanics were implemented in Task 001.
- **Authority:** the C# simulation owns canonical game state; the ASP.NET host exposes operations/state; the browser visualizes state and submits commands. Simulations must remain usable headlessly without a running browser.
- **Approved frontend (8 November 2026):** React for controls/data screens, Babylon.js for 3D views/globes, and PixiJS for 2D maps, operational views, and plans. Retain TypeScript/Vite. The C# simulation remains authoritative; 2D/3D views describe the same world. See `docs/FRONTEND_ARCHITECTURE.md` for boundaries, packages, and validation.
- PostgreSQL, WebSockets, Docker workflows, and variable-fidelity implementation techniques remain **candidates**. MapLibre GL JS is **deferred**, outside the approved initial frontend stack. Private Tailscale Serve access is approved and deployed for the skeleton; further dependencies still need a concrete task and appropriate review.
- **Spatial direction:** true 3D galactic space and continuous planetary coordinates are stated project directions. Symbolic/technical maps and data-driven presentation are preferences for rendering and interaction; they do **not** imply a two-dimensional simulation world.
- Use **Quasar** consistently; **Qasar** is an obsolete spelling.

## iPhone browser pillar

- **Approved (8 November 2026):** frequent iPhone browser play is a core Quasar pillar. The owner's iPhone 17 Pro is the primary phone test device; iOS/browser version remains to be recorded. See `docs/MOBILE_AND_REMOTE_ACCESS.md`.
- Design core inspection, map selection, orders, pause/advance controls, and management flows for readable touch use in portrait, with useful landscape and desktop layouts. Preserve depth through focused views and drill-down. Avoid hover-only, mouse-only, or keyboard-required workflows; respect safe areas, software keyboards, text scaling, and browser zoom.
- Budget mobile GPU, memory, battery/heat, and bandwidth. Load/render needed views and stop unnecessary work while hidden. Treat backgrounding, screen lock, network changes, and reconnect as normal. The backend remains authoritative; do not silently replay uncertain orders or advance canonical time in a browser tab.
- Validate on the actual iPhone 17 Pro as well as desktop; responsive emulation is not proof of mobile rendering or lifecycle reliability. Safari is the recommended initial browser baseline, not a confirmed installed version. Do not claim measured capacity or phone readiness without device checks.
- **Approved remote skeleton (8 November 2026):** private Tailscale HTTPS proxies to the published ASP.NET/client host on loopback 5080. The owner's connection address is in the ignored `docs/LOCAL_PROJECT_CONTEXT.md`, not the public repository. The owner confirmed iPhone access. Preserve other Serve routes. Unattended startup remains unresolved; see `docs/HOSTING.md`.

## Project knowledge and shared tracking

- For Quasar, the shared Google Game Design Record is the persistent source for project knowledge, design discussions, questions, decisions, progress, and handoffs.
- Do not use Obsidian for Quasar: do not search, read, create, or update vault notes for this project. This project-specific instruction overrides general Obsidian guidance for Quasar only; other projects and global integrations are unaffected.
- Repository documents support implementation and reference the shared Google Doc. Code and tests describe implemented behavior; dated snapshots are offline references, not separate live project records.
- Preserve the distinction between confirmed direction, candidate approaches, and open questions. Updating the shared record does not approve a proposed product or architecture choice.

## Project scope and sources

- Read `README.md`, `docs/DECISIONS.md`, and the relevant project-plan section.
- Read `docs/SOURCES.md` and the linked Game Design Record before consequential
  game-design or architecture work. Use the live Google Doc when current source
  content matters; the dated local snapshot supports offline reference. Preserve
  its confirmed/candidate/open distinctions. True 3D galactic space and continuous
  planetary coordinates are stated directions, not optional assumptions.
- Task 001 initializes tooling only. Do not silently continue into game feature
  work; implement the next task when requested.
- Keep proposals distinct from approvals. Do not infer requirements from
  exploratory notes or adopt a major stack substitution without approval.
- Keep the simulation independent of ASP.NET, the browser, storage frameworks,
  and map libraries. Do not add speculative abstractions or domain assemblies.
- Preserve existing work and data. Do not move the OneDrive checkout, start or
  reconfigure Docker, change system software, or publish a GitHub repo merely to
  make local development work.

## Shared design sources and collaboration

**Game Design Record (Google Docs):**
Access details are in the owner's ignored `docs/LOCAL_PROJECT_CONTEXT.md` when
working on that PC. Public clones use `docs/SOURCES.md` and public decision/plan
records; ask the owner for private context when a design task requires it. Do not
publish the live document link or full private snapshots to make access easier.

- Preserve distinct roles for sources: the **live Google Game Design Record** is Quasar's shared source for project knowledge, design, and tracking; `docs/SOURCES.md` indexes source access and snapshots; `docs/DECISIONS.md` tracks technical decision status; code/tests document implemented behavior; `docs/PROJECT_PLAN.md` tracks implementation milestones. Keep relevant repository records consistent with the shared document without treating local copies as a competing project knowledge system.
- Start by inspecting `docs/SOURCES.md`. It already refers to the Game Design Record and a dated local snapshot. Do not create competing copies or pretend the snapshot is current. Where a design decision depends on current text, prefer a **verified live read**, and report the source and date/version used.
- A Google Docs URL or a ChatGPT connection does **not** prove that this Codex session can call Google APIs. Discover existing connectors/MCP capabilities first; verify read access by fetching the actual title and headings. Report read and write capability **separately**. Inspect the existing Google integration before installing or reconfiguring anything. Do not perform a dummy edit to prove write access.
- If live access is unavailable, use the existing dated snapshot for clearly labeled offline reference, and identify exactly what user action is needed to re-establish a live connection. Never represent exported files as automatic synchronization.
- Changes to the Game Design Record require an explicit user instruction for the specific change; preserve structure, context, and distinctions between **confirmed**, **candidate**, and **open** items. Confirm execution/results before saying an external document was modified. Do not insert unapproved concepts as settled rules.
- ChatGPT, Codex, and the owner do not automatically share hidden conversation context or filesystem state. Provide concise, reusable design and implementation handoffs in repository documents, and note which external source was actually updated.
- When evidence or sources conflict, surface the discrepancy to the owner rather than silently selecting the most convenient interpretation.

## GitHub and ongoing notes

- **Approved by the owner (8 November 2026):** the codebase is public on GitHub at `Docbuchanan84/Quasar`, under the explicitly selected MIT license. Initial repository creation, baseline commits, publication, CI, and ongoing implementation notes are authorized.
- For completed authorized project work, update `docs/DEVLOG.md` and relevant decision/plan/setup records, make coherent descriptive commits, and push the focused task branch. Use `codex/<task>` branches and pull requests with passing CI for subsequent work. Report commits, checks, and publication status. Do not silently merge or force-push shared history.
- Keep Google Docs as the owner's shared design record. Repository notes are the public implementation trail. Mark decisions Proposed / Approved / Rejected / Deferred and distinguish implementation status from approval.
- Never stage private connection details, credentials, owner-only source documents, local audit backups, generated output, or live game data. `docs/LOCAL_PROJECT_CONTEXT.md` and `docs/sources/` are ignored. Review staged files before each public push. See `CONTRIBUTING.md`.

## Hosted preview commands

The private host serves built React assets, `/api/health`, and the authoritative
System Lab snapshot/command API from one ASP.NET origin. Use
`scripts/Publish-HostedApp.ps1`, `scripts/Stop-HostedApp.ps1`, and
`scripts/Start-HostedApp.ps1` for updates; keep the host terminal open. Published
files live under `%LOCALAPPDATA%\Quasar\host`, outside the OneDrive checkout.
Do not run the development backend on port 5080 while the published host uses it.

## Commands on this Windows PC

Use PowerShell 7 (`pwsh`). These scripts resolve paths from their own location:

```powershell
.\scripts\Setup.ps1        # Locked restores; user-local SDK install if absent
.\scripts\Verify.ps1       # Build, xUnit runner, .NET format, frontend lint/build
.\scripts\Start-Server.ps1 # Foreground host at 127.0.0.1:5080
.\scripts\Start-Client.ps1 # Foreground Vite at 127.0.0.1:5173
```

For individual .NET commands, dot-source the session environment first:

```powershell
. .\scripts\Use-DevEnvironment.ps1
dotnet --version
dotnet test Quasar.slnx
```

The machine's system SDK inventory differs from Quasar's pinned local SDK;
do not assume a plain `dotnet` resolves the right version. Quasar's local SDK is
`%LOCALAPPDATA%\Quasar\dotnet`; an explicit alternate local
root can be supplied using `QUASAR_DOTNET_ROOT`. Do not edit the system PATH.

## Verification and reporting

- Run checks appropriate to the change. After source/configuration changes,
  `scripts/Verify.ps1` is the standard full check, including the renderer package
  loading and browser-bundling check. It does not establish GPU performance.
- Task 001 has no permanent tests. A "no test available" result is not a passing
  behavior-test suite. Add focused tests alongside real simulation behavior.
- Keep `package-lock.json` and `packages.lock.json` files under version control.
  When deliberately changing packages, update locks using `npm install` or
  `dotnet restore --force-evaluate`, then rerun locked verification.
- The approved Babylon.js/PixiJS packages are installed in the client. Do not add
  further database, map, WebSocket, coverage, or benchmark packages until a
  concrete task needs them. Do not require cloud services for local development.
- Keep generated output, credentials, saved games, and live databases out of Git.
- Summarize what changed and what was actually verified. Record any unresolved
  setup issue in `docs/DEVELOPMENT_SETUP.md`.

## Development practices and owner collaboration

- The project owner is learning C# and software engineering. Explain **why** important changes are made using accessible language and correct terminology; provide enough context to understand the code without pages of tutorial filler.
- For each task: establish the active objective and smallest deliverable; inspect existing code and decisions; separate facts from hypotheses; implement only requested scope; verify; then give a succinct handoff and **one next step**. Support exploration but flag when it distracts from the active task.
- Keep abstractions and file/module boundaries proportionate to a real problem. Avoid speculative frameworks, early event-sourcing commitments, universal ECS/schema decisions, distributed services, or domain assembly proliferation without explicit approval.
- Prefer readable, idiomatic C# and TypeScript, descriptive names, simple dependencies, and comments explaining invariants or non-obvious decisions. Keep simulation changes testable and deterministic where appropriate; include invalid-input and boundary tests for real behavior.
- The user alone approves material product/architecture decisions. Mark entries in `docs/DECISIONS.md` as **Proposed / Approved / Rejected / Deferred** with rationale and date; testing a candidate does not constitute approval. Implementation status is separate from decision status.
- Never claim specific performance or scale capabilities without measurement. In particular, the time architecture (seconds to long jumps, simultaneous domains, look-ahead and variable fidelity) requires experiments before selecting a definitive implementation.
- Avoid destructive operations, force pushes, silent Git commits/publishing, broad installs, and edits to shared documents outside the explicit task scope. Protect credentials and use official authenticated connection flows.
- End significant work with: changed files, what was verified (including commands/results), decisions made vs still pending, blockers, and the single recommended next task. Do not claim checks passed if they were not run.

## Task 002 implementation update (2026-11-08)

The owner authorized and implemented the System Lab vertical slice. Treat the
Task 001 descriptions above as historical. Current behavior and contracts are
in `docs/SYSTEM_LAB.md`; current scope/next task are in `docs/PROJECT_PLAN.md`.
Simulation/session behavior tests now exist. The host exposes authoritative game
commands as well as health; maintain its same-origin JSON validation and private
network access. No permanent scheduler, save format, or new runtime dependency
was selected by implementing the provisional lab mechanics.
