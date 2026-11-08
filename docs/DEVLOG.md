# Development log

## 2026-11-08 — prerequisite integration for Agents 1–5

- Owner explicitly requested coherent merging of all five recent agent tasks,
  with latitude to adjust conflicts. Consolidated the System Lab baseline,
  Agent 2 celestial service, Agent 3 spacecraft movement, Agent 1 time expansion
  and Agent 5 navigation before publishing Agent 4 course plotting separately.
- Preserved shared XYZ transforms/reference geometry, authoritative trajectory
  samples and gravitational movement, all nine time intervals, gesture-safe
  selection and the corrected Z-up 3D camera/focus controls. Retained both sets
  of frontend verification scripts when resolving package/script conflicts.
- Updated the interval regression and HTTP smoke script: an unordered ship now
  coasts under gravity. Exact clock checks compare positions with the existing
  authoritative future query instead of expecting stationary/Idle state.
- Verification and branch/PR publication recorded in AGENT_INTEGRATION.md.
  No runtime dependency or new product decision. Hosted game unchanged.

## 2026-11-08 — Agent 3 spacecraft trajectories and movement

- Inspected the checkout and Agent 2 working tree as well as their commits; both
  were clean before task changes. No fleet/engine/mass/fuel model exists. Created
  `codex/spacecraft-trajectories` in an isolated worktree from Agent 2's `2f79207`;
  navigation/time-control branches and the owner's running hosted world are untouched.
- Added separate pure Lambert planning, universal-variable gravitational coasts,
  immutable committed execution plans, event-ordered advancement, cancellation
  retaining momentum, explicit parent/child SOI-patched legs, delta-v budgets and
  future fuel-estimation adapter. No frame integration or new runtime dependency.
- Integrated read-only preview/state APIs and same-origin revision-checked
  travel/cancel commands. The lab begins on a gravitational coast and uses
  six-hour impulsive transfers. Both maps draw server samples from the same conics.
  Corrected course status messaging. Fixed planets and abstract rendezvous remain
  lab limitations, explicitly documented in SPACECRAFT_TRAJECTORIES.md.
- Verified `scripts/Verify.ps1`: 64 xUnit cases pass, zero build warnings/errors,
  .NET format, frontend lint, renderer loading/browser bundling, two coordinate
  tests and production TypeScript/Vite build. Existing lazy 3D chunk-size advisory
  remains; it is not a measured GPU result.
- Isolated port-5093 HTTP tests passed: Agent 2 celestial contract, all 65 trajectory
  samples equal authoritative position/velocity queries, preview leaves revision/
  world/history unchanged, cancel/replan, invalid inputs, origin denial, exact
  tick strings and arrival interruption with no maps open.
- Chromium smoke checks rendered curved routes in Pixi 2D and Babylon 3D,
  advanced one hour and cancelled without changing position/speed at cancellation;
  desktop and 393x852 layouts inspected, zero browser console errors. Screenshots
  remain ignored under output/playwright. This is not actual iPhone validation.
  Disposable browser/host were closed; the live host was not redeployed.
- Preliminary Release measurement, 1,000 ships sharing one plan: cold solve
  8.925 ms, 1,000 position queries 1.085 ms, first no-event advance 2.065 ms,
  simultaneous arrivals 1.030 ms. Not a capacity guarantee; JIT/shared route limit it.
- Live Google design record read verified (title and headings) on 8 November.
  Edit tool available, write execution untested; no external design-record edit.
  Physics is task-scoped, not approval of n-body, finite-thrust or automatic route
  search. Fuel remains unavailable. Next: define propulsion/mass data before
  fuel spending/finite thrust. Review this branch against Agent 2's branch.

## 2026-11-08 — Agent 2 celestial mechanics and position service

- Added immutable celestial definitions/state vectors, pure elliptic Keplerian
  propagation and simulation-owned hierarchical queries. Explicit km, km/s,
  radians, GM and signed ticks; analytical velocity, derived periods and
  approximate Laplace SOI. No runtime packages, per-second motion loop or UI
  authority. Catalogue replacement invalidates descendants/derived metadata.
- Integrated lab snapshots and a read-only arbitrary-time HTTP query. Kept fixed
  lab planets/travel behavior; moving-target orders/course plotting remain outside
  scope. Both maps share scaling/projection/reference geometry.
- Used an isolated worktree from `8c1a722` to preserve simultaneous unrelated
  time-control/map work. Branch `codex/celestial-mechanics`; no hosted session
  restarted or republished.
- Verification: 48 xUnit cases passed, including 27 new celestial cases; full
  `scripts/Verify.ps1` build/format/lint/renderers, two coordinate tests and
  frontend build passed. New celestial and existing System Lab HTTP smoke passed
  on isolated loopback 5091. A first coordinate assertion exposed ordinary
  multiplication/division rounding and was corrected to use a tolerance.
- Measured 10,001 bodies across 20 uncached extreme-time bulk queries at
  133.035 ms total in a warmed Release run here; tested a 10,000-level hierarchy
  without recursion. Synthetic headless results, not phone/rendering capacity.
- Read live Game Design Record on 8 November 2026 (revision prefix `AHj4eMQHPFgN`).
  Early pre-production prose is historical relative to its Task 002 update.
  Write tools present; write access untested and external record unchanged.
- DECISIONS distinguishes requested scope from reversible choices;
  [CELESTIAL_MECHANICS.md](CELESTIAL_MECHANICS.md) documents interfaces and limits.
- Next task: recoverable versioned save/load including celestial elements/epochs.

Keep concise entries for completed work. Include the date, result, validation,
decisions/pending questions, and one next step. Commit the entry with the related
change so Git history and notes remain connected. This is an implementation log;
the owner's shared Google Doc remains the design discussion record.

## 2026-11-08 — Public repository and version-control foundation

- Owner approved a public GitHub repository and explicitly chose MIT licensing.
- Prepared `main`, a reproducible CI verification workflow, contribution guidance,
  and this ongoing development log. Actions are pinned to reviewed commit SHAs.
- Separated private deployment addresses, owner-only source access, and full
  document snapshots from the public source. Local copies remain recoverable.
- Existing foundation: headless C#/.NET 10 library; ASP.NET serving a built React
  skeleton and read-only health endpoint; React/Babylon.js/PixiJS approved roles.
- Mobile play is a pillar; the owner confirmed the private skeleton on iPhone 17
  Pro. No simulation clock, game-state API, maps, or persistence exists yet.
- Validation: local build/format/lint/type/bundle and renderer checks pass. The
  initial [GitHub verification run](https://github.com/Docbuchanan84/Quasar/actions/runs/42510877465)
  passed on a fresh Windows runner. Anonymous Git access and connected-plugin
  README readback were verified; the working branch tracks its GitHub remote.
- Repository controls: PRs and passing up-to-date `verify` checks are required on
  `main`, including for the owner; force-push/deletion are blocked. Squash merging
  and merged-branch cleanup are enabled. Secret scanning/push protection and
  dependency vulnerability alerts are enabled. A solo owner is not required to
  obtain an impossible self-approval.
- Limit: private hosting currently uses a foreground process; unattended startup
  remains unresolved. The simulation test project has no behavior tests yet.
- Next implementation task: the minimal headless universe clock with focused
  behavior tests. Durable private hosting is a separate operational follow-up.

## 2026-11-08 — Task 002 System Lab

- Replaced the clock-only milestone with the owner's requested playable vertical
  slice: Helios, Iris, Nereid, and Pathfinder; fixed identities and true XYZ state.
- Added headless analytic travel, retargeting, checked elapsed time, exact arrival
  history/interrupts, and snapshots independent of web/rendering packages.
- Added a serialized in-memory host session and same-origin JSON HTTP boundary.
  Once/Continuous Play processes selected intervals; Stop cannot lose to a tick.
  The browser control lease expires after five seconds without renewal.
- Added shared React selection/inspection/orders/history, lazy Pixi/Babylon maps,
  pan/zoom/rotation/selection and explicit touch controls, illustrative surface
  inset, hidden-view cleanup, no unchanged Pixi redraws, and map-load recovery.
- Validation: `scripts/Verify.ps1` passed: 21 xUnit cases, zero .NET warnings/errors,
  formatting, frontend lint/type/build, and renderer loading/bundling. Vite's lazy
  3D chunk warning remains (~1.09 MB raw/~265 KB gzip); no runtime packages added.
- `scripts/Test-SystemLabApi.ps1` passed against isolated loopback 5090 and deployed
  private HTTPS: snapshots, one-shot execution, invalid input, stale revision,
  origin/content-type rejection, no-store, and unknown API 404.
- Browser checks passed on actual Chromium renderers: order/hour advance, arrival,
  Once/Continuous/Stop, refresh retention, shared inspector XYZ, both canvas/mesh
  selection, camera controls, reference toggle, eight view switches with one
  canvas, illustrative surface, offline lease expiry/reconnect, and map-import
  failure containment/reload. Desktop and 402/320 px portrait plus landscape
  layouts had no horizontal overflow. Screenshots were visually reviewed.
  Deliberate offline and blocked-map tests generated expected network errors;
  ordinary loaded views had no browser warnings/page errors.
- Measurement: 100,000 idle 30-day C# advances took 6.366 ms locally (Release test).
  This demonstrates the tested analytic path, not a universe-scale guarantee.
- Deployed using existing publication/Stop/Start scripts. Release retained outside
  the checkout with previous releases and pre-task deployment metadata preserved.
  Health reports system-lab over private HTTPS; deployed orders/clock/3D and shared
  XYZ were checked. Tailscale configuration compared equal before/after; other
  services/routes were untouched. Hosting remains a managed foreground session.
- Shared design source: live Game Design Record read on 2026-11-08. The authorized
  progress update is recorded there; private links/snapshots remain excluded.
- Approved scope: System Lab and Once/Continuous controls. Provisional: stationary
  bodies, 25 km/s straight-line travel, reference guides, runner cadence and lease.
  No scheduler, persistent save format, calendar, database, or terrain model selected.
- At implementation handoff: actual iPhone 17 Pro gameplay/rendering/touch/lifecycle
  checks and exact iOS/browser version were pending. The subsequent owner check
  below confirms basic portrait 2D use; broader device coverage remains open.
  No functional blocker for the deployed lab; phone capacity remains unmeasured.
- Next: small versioned save/load with atomic writes and recoverable backups.

## 2026-11-08 — Owner phone acceptance and documentation follow-up

- The owner reported: "It looks so good already and works just how I want" and
  requested a thorough documentation update, supplying two phone screenshots.
- Recorded actual-device evidence separately from automated tests and the owner's
  broader acceptance: portrait 2D tactical rendering, connected host, selected
  Pathfinder, stopped/processing Continuous controls, and changing position/depth.
  The two visible clocks differ by five simulated minutes; no wall-clock/GPU
  performance is inferred. Neither image shows 3D or an arrival/gesture sequence.
- Preserved original screenshots and SHA-256 provenance under ignored source
  evidence. Private address/browser content was not staged or published.
- Reconciled README, mobile/frontend/hosting/setup guidance, source index,
  implementation contracts, milestones, decisions, and agent handoff guidance.
  Removed stale current-tense claims that maps/game endpoints do not exist.
- Marked Task 002 implemented/deployed and its initial phone experience accepted.
  Provisional physics/time mechanisms remain provisional; the feedback does not
  authorize a new feature or settle permanent architecture.
- Recorded the owner-requested post-deployment reset: only Quasar restarted;
  verified zero clock, paused, ship idle, four objects and one initial event.
  Later owner play is preserved; this documentation work does not reset the host.
- Implementation CI passed for `32d45a8`:
  [run 42517246183](https://github.com/Docbuchanan84/Quasar/actions/runs/42517246183).
  Follow-up documentation is on [PR #2](https://github.com/Docbuchanan84/Quasar/pull/2);
  consult its live checks/merge status rather than treating a dated note as current.
- Updated the shared Google Game Design Record with the owner's acceptance,
  screenshot observations, and narrowed open device checks; readback verifies
  the change. This was an explicit documentation request, not a dummy access edit.
- Validation for this follow-up: documentation/link/status consistency, screenshot
  copy hashes and Git-ignore checks, staged privacy/whitespace review, and PR CI.
  No new runtime behavior tests or deployment are needed for documentation only.
- Still open: exact iOS/browser versions, physical-phone 3D, full gesture/order/
  arrival flow, landscape/scaling, lock/app-switch recovery, cellular-only access,
  measured capacity/heat/battery, save/load, and unattended host startup.
- Next development task: recoverable versioned save/load; no Task 003 work started.

## 2026-11-08 — White-screen report / stopped-host recovery

- Owner reported a white screen after previously successful phone play.
- Inspection found no running Quasar/.NET host process and loopback 5080 refused
  connections. Serve routes still targeted their existing ports. The host log
  contained no termination cause; the reason for process exit is unconfirmed.
- Started the existing published release with Start-HostedApp.ps1. No source,
  release, Tailscale, startup-service, or system configuration change was needed.
- Verified private HTTPS page/health/snapshot responses, stage system-lab, four
  fixture objects, paused clock at zero, and rendered browser controls/map. The
  browser reported zero console warnings/errors during this recovery check.
- State from the stopped process was already lost: the lab is in-memory. Recovery
  started the fixture; it did not restore the owner's earlier session.
- Remaining availability limitation: hosting still depends on a managed foreground
  process. No permanent unattended supervisor was installed. Startup reliability
  now warrants an operational follow-up before relying on continuous access;
  recoverable save/load remains the next game-development task.

## 2026-11-08 — Repeat outage / phone download prompt

- The owner still saw an empty page and supplied a phone screenshot offering
  `document.txt`. Inspection again found no host process; Serve returned empty
  502 responses for `/` and `/index.html`, with no Content-Type. Healthy HTML was
  not being served. The exact phone browser download handling remains unproven.
- The previous agent-managed foreground start had not remained available. Launched
  the existing Start-HostedApp.ps1 in its own separate visible PowerShell console
  through the approved GUI launch, and verified the host is its child process.
  The console remains open for the owner to see/control. No hidden service,
  scheduled task, auto-start, release rebuild, or Tailscale change was introduced.
- Verified private HTTPS page status 200, Content-Type text/html, no attachment
  header, referenced JS/CSS status 200 and correct MIME types, health ready,
  and browser map/controls with zero warnings/errors. Another fresh navigation
  was supplied to bypass the old failed phone navigation/download prompt.
- Updated hosting/setup guidance to require a separate visible terminal instead
  of relying on a temporary agent command. The already-stopped in-memory world
  could not be recovered; a fresh fixture starts with the host.
- The owner opened the fresh navigation and confirmed "Game loads now" on the
  phone. This confirms recovery of the reported blank/download experience.
- Still open: persistence and durable unattended supervision. Keep the host
  window open in the meantime; this is not a Windows service or reboot recovery.

## 2026-11-08 — Expanded simulation time controls

- Added 5 seconds, 30 seconds, 5 days, and 15 days in ascending order to the
  existing native selector and server duration validation. Retained all five
  earlier intervals, the one-hour default, and numeric seconds in the API.
- Inspection confirmed that the authoritative C# clock already uses checked
  integer ticks, analytic movement from departure state, and exact arrival
  interruption. These paths need no change or per-second/frame processing.
  Arrival still stops early and discards unused time; Play starts a fresh interval.
- Extended simulation/session coverage to 51 passing cases, including all nine
  intervals, 1,800 repeated advances with interval changes, matching movement and
  event history, short/long arrival boundaries, Once/Continuous, Stop/Play resume,
  paused wall-clock time, lease expiry, and invalid duration atomicity.
- `scripts/Verify.ps1` passed: locked restore, Release build (zero warnings/errors),
  all 51 xUnit cases, .NET formatting, frontend lint, renderer loading/bundling,
  TypeScript and production build. Existing large lazy 3D chunk warning remains.
- `scripts/Test-SystemLabApi.ps1` passed against an isolated loopback 5090 host;
  it now checks every interval and transitions through the real HTTP runner.
- Playwright/Chromium checks passed for selector values/labels/order, exact Once
  advances for all nine intervals, all four new Continuous/Stop/resume flows,
  exact arrival interruption, and desktop/402/320 px layouts. Screenshots were
  visually reviewed at 320 px; no page errors, console errors, or warnings.
  Browser artifacts remain ignored. This is not a new physical-iPhone check.
- Live Google Game Design Record read verified its title and time/System Lab
  sections on 8 November 2026. Its existing arrival/Stop/analytic semantics agree
  with this extension. Edit tooling is exposed; no write was attempted because
  this task does not explicitly request a shared-document update.
- No dependencies, simulation schemas, or save formats changed; the lab still
  has no persistent save/load. The running private host and its in-memory play
  state were preserved. Only the isolated verification host was started/stopped.
- Publication uses a focused branch stacked on the unmerged Task 002 PR #2;
  no merge, force-push, deployment, or private-source publication is performed.
- No implementation blocker or new architecture choice. Next: recoverable
  versioned save/load under a separately scoped task.

## 2026-11-08 — Simplify map navigation and repair 3D camera controls

- Implemented the owner's navigation task in `SystemMap.tsx`, `SpaceMap.tsx`,
  `mapGestures.ts`, `spaceCamera.ts` and map styles. Removed four directional
  buttons from both views and the redundant 3D rotate-step button. Kept compact
  zoom alternatives, consistent **Reset View**, and added **Focus Selected**.
- Diagnosed default Y-up versus Quasar's XY/Z frame, fixed-distance pan speed,
  origin-only orbit pivot, persistent motion after Reset, and tap/drag ambiguity.
  Use Z-up, screen-scaled panning, proportional/natural bounded zoom, reduced
  orbit inertia and explicit selected-object focus/follow. Panning/two-finger
  navigation releases follow while preserving selection. No renderer replacement.
- Shared gesture intent tracking protects selection from jitter, drags and pinch
  handoff; cancellations/lost capture/blur clear the gesture. Babylon retains its
  camera input/capture/math; Pixi retains its Pointer Events path. Browser testing
  also caught Babylon's attach-time bridge overwriting a prepended middle-button
  pan entry; the corrected mapping has an attachment regression test.
- Visual review also corrected the 3D home angle so positive X points right,
  matching the 2D home view. A projection-direction regression assertion covers it.
- `scripts/Verify.ps1` passed: locked restore, Release build (zero warnings/errors),
  51 xUnit cases, .NET formatting, frontend lint, renderer loading/bundling,
  **nine new Node/Babylon navigation tests**, TypeScript and production build.
  The existing lazy 3D bundle-size warning remains; no new runtime dependency.
- Playwright CLI passed **37 browser checks** on real Pixi/Babylon canvases:
  desktop mouse orbit/pan/wheel/jitter/reset/focus, native Chromium touch tap,
  swipe, two-finger pan, pinch/handoff/cancel, shared selection, course/reference
  overlays and following a moving ship. Tests used an isolated loopback 5092 host.
  Desktop and 393 × 852 screenshots were visually reviewed; artifacts are ignored.
  The production build also rendered both maps and exercised 3D focus/reset in
  Chromium against that isolated host without page errors.
  These are not physical-iPhone/Safari tests or measured GPU capacity claims.
- Updated README, frontend/mobile/setup guidance, approval and plan records, and
  [MAP_NAVIGATION.md](MAP_NAVIGATION.md) with control mappings, diagnosis and
  reproducible checks. The live Google Game Design Record was read on 8 November
  2026 (title/interface/mobile headings verified). Edit tools are exposed but no
  external-document write was requested or attempted.
- Existing time-control work was preserved as the parent commit. Publication is
  a focused stacked PR; no merge, force-push, hosted deployment/restart, system
  configuration change, private-source publication, or live-world mutation.
- No implementation blocker or new product/architecture proposal. Next: validate
  these navigation changes on the owner's iPhone 17 Pro and record its versions.
