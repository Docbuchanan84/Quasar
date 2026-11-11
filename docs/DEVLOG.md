# Development log

## 2026-11-10 — Headless 1,000-ship travel benchmark

- A fresh Solar System fleet completed 3,000 finite-burn station approaches.
  Each run handled 9,000 events and queried position and velocity one million
  times. See the [fixture and results](SPACECRAFT_TRAJECTORIES.md#repeated-fleet-travel-benchmark).
- Indexing gravity children, reusing each course's boundary schedule, and
  removing allocations from segment lookup preserved event ticks and the query
  checksum. At six planner workers, median event processing fell from 689 ms
  to 100 ms and allocations fell from 1,579.13 MiB to 2.84 MiB.
- Twelve detached planner workers solved the 3,000 locked-duration routes in
  1.40 s median on the test PC. `scripts/Verify.ps1` passed 291 xUnit tests and
  11 client tests, formatting, lint, renderer loading, and production bundling.
- The 10,000-ship scale run verified 30,000 arrivals and 10 million queries.
  Event processing reached 9.62 s median with 70,800 staggered advances.
  The next scheduling experiment should target the repeated fleet scans for
  potential free-coast gravity changes. The follow-up is tracked in
  [GitHub issue #41](https://github.com/Docbuchanan84/Quasar/issues/41).
- The owner requested phone play. The CI-verified benchmark revision `64b3453`
  was published to the existing private preview. A verified save backup and a
  disposable restore preceded the switch. The live host restored the same
  paused checkpoint and world. Private HTTPS served the game and its assets,
  and a 393 px browser viewport displayed the active campaign and course.

## 2026-11-10 — Courses across gravity systems

- The owner clarified that the computer must plan between locations across
  gravity systems and rebalance unlocked course controls. Callisto was blocked
  by a single-primary guard, despite the starting torch's capability.
- Finite departure and braking burns now support primary changes inside RK
  steps. Analytical coasts use continuous SOI detection and actual-interval
  surface clearance. Full forward arrival tolerances remain 10 m and 1 mm/s.
  Tight-orbit departures use damped shooting. A near-full-thrust adjustment
  aligns exact rocket-equation burn impulses with integer clock ticks.
- All 40 modeled destinations returned verified routes from the starting state
  in a disposable sweep (0 failed; maximum observed 4.021 s). Orbit-to-moon,
  moon-to-planet, station and outer-system routes are covered. This is measured
  fixture coverage, not a promise that every fuel/lock/geometry combination is
  feasible. Searches stop after four seconds and retain completed candidates;
  bounded caches support repeated slider requests.
- Reserve edits retain the requested minimum while asynchronous results arrive;
  actual remaining fuel is reported separately. This prevents a completed
  calculation from moving the control during an ongoing edit.
- High-reserve recalculations probe fuel-appropriate durations before polishing
  a previous fast route, so the bounded search retains a viable slower option.
- Checkpoint v7 rebuilds transitions and validates primary, fuel and history.
  Versions 1–6 remain readable. Callisto regressions cover execution, momentum,
  cancellation around every boundary, HTTP restart and Continuous interrupts.
- `scripts/Verify.ps1` passed 291 xUnit tests and 11 Node checks, formatting,
  lint, renderer loading and production bundling. Disposable Chromium passed
  Callisto's linked controls and locks, exact confirmation, 3D review and
  393 px portrait layout. The existing course browser scenario also passed.
  Physical iPhone validation remains separate.
- The live Google design record was read on 10 November (title and revision
  verified); its earlier release summary is historical. It was not modified.
- PR review found two edge cases: small-body insertion presets must fit inside
  the destination SOI, and zero-GM stations need the explicit 10 km approach
  fallback. Both are corrected, with six focused regressions covering Bennu,
  Deimos, Phobos (including HTTP) and both stations.

## 2026-11-10 — Starting Pathfinder exploration torch

- Every new campaign starts Pathfinder with the exploration torch and 20,000 kg
  of reaction mass. The owner requested this as the default, replacing the
  chemical starter. Existing checkpoints retain their serialized equipment.
- Explicit chemical fixtures preserve the existing exhaustion, low-reserve,
  finite-course cancellation and restore regressions. New-game tests execute
  Mercury, Venus and Mars approaches from both the initial epoch and two hours
  later; HTTP tests create two campaigns and plan Mars without a refit.
- `scripts/Verify.ps1` passed 261 xUnit tests and 11 Node checks, formatting,
  lint, renderer checks and the production build. Both disposable browser
  scenarios passed: linked controls and locks, exact order confirmation,
  Continuous interrupts, arrival wording, 3D review and a 393 px portrait
  layout. Actual iPhone validation remains separate. The planner still uses
  the existing bounded single-primary model and does not support every route.

## 2026-11-10 — Exploration torch and linked course controls

- The owner requested useful routes for Pathfinder, a torch-like drive, and
  linked controls with locks. A paused-coast refit supplies 300 kN thrust,
  1,000 km/s exhaust velocity and 20,000 kg of reaction mass. The client uses
  the installed capability without the previous 25 km/s limit.
- Automatic courses support burns up to one day. Full forward arrival checks
  retain their tolerances while refinement can continue around tick rounding.
  The torch supplies executable Mercury, Venus and Mars approaches from the
  tested two-hour starter state. Gravity handoffs remain outside this planner.
- Travel time and minimum reserve adjust together. Locks preserve constraints,
  and conflicting constraints disable order confirmation. The chosen adjusted
  route uses the existing exact-preview confirmation contract.
- `scripts/Verify.ps1` passed 257 xUnit tests and 11 Node checks. Disposable
  Chromium verified refitting, linked controls, conflicts, exact confirmation,
  3D review and a 393 px portrait layout. Long-burn cases cover fuel spending,
  cancellation and checkpoint restore.

## 2026-11-10 — Propulsion-aware automatic courses

- [Issue #28](https://github.com/Docbuchanan84/Quasar/issues/28) makes automatic
  course confirmation depend on the installed engine, current mass and fuel,
  and forward verification of the requested arrival state.
- The course panel shows departure and braking burns, propellant cost and
  reserve, and arrival conditions. The bounded search supports direct
  approaches and same-primary Earth or Moon orbit presets.
- Finite courses interrupt at burn boundaries and coast from the state
  achieved at final cutoff. Checkpoint v6 restores the same active course and
  remaining propellant across departure, coast, and braking.
- `scripts/Verify.ps1` passed 236 xUnit tests and 11 Node checks. The focused
  planner, confirmation, and HTTP restart run passed 19 cases. Disposable
  Chromium covered feasibility, exact selected-route confirmation, desktop
  2D/3D review, and a 393 px portrait viewport. All seven disposable campaign
  browser phases passed. The next phone check is a
  complete approach from preview to final cutoff on the physical device.
- The merge review added coverage for JSON restore and cancellation at burn
  boundaries, executed orbital insertion, Continuous interrupts, and the search
  evaluation ceiling. `scripts/Verify.ps1` passed 244 xUnit tests and 11 Node
  checks. The review found two remaining issues in PR #34. Legacy orders can
  bypass fuel accounting in a propulsion-equipped campaign, and one-second
  powered samples can miss a brief gravity boundary crossing.
- The follow-up closes both order paths for equipped ships and verifies powered
  clearance between samples. The fastest stock Earth approach displays its
  10.141 kg reserve and a warning below 1% of departure propellant. Approach,
  orbital insertion, and legacy arrival have distinct clock and history labels.
- `scripts/Verify.ps1` passed 250 xUnit tests and 11 Node checks. Chromium passed
  the course flow on desktop and at 393 px and all seven legacy campaign phases
  with a checkpoint written by the pre-fix server. On the same workstation,
  warm Earth preview p95 fell from 21.2 to 14.4 ms and the 3 kN fixture from
  260.9 to 45.8 ms. These are fixture measurements.

## 2026-11-10 — Automatic SOI crossing audit

- Integrated host-owned Continuous play from PR #32. An automatic primary
  change stops the run and appears in the clock notice and event history.
- Replaced one-minute sampling with acceleration-bounded subdivision. A
  500 km/s Earth grazing fly-through penetrates for under one second with both
  minute endpoints outside. Tests cover that entry, brief exit near apoapsis,
  tangencies, overlapping spheres, coincident arrival and entry, inertial
  continuity, and equal results after resuming to the same tick in large or
  small advances. The high-speed case also exposed and fixed hyperbolic
  propagation overflow when seeding the solver bracket.
- SQLite tests restored formats v1–v5. The coast prediction horizon accepts
  30 days exactly and explicitly rejects the next tick.
- A 30-day event-free advance with 1,000 eligible coasts took 5.785 ms median
  in Release, down from 66.996 s with the minute scan. The
  [trajectory notes](SPACECRAFT_TRAJECTORIES.md#thirty-day-automatic-coast-benchmark)
  include the smaller fleets, fixture and scheduling opportunities.
- `scripts/Verify.ps1` passed with 232 xUnit tests and 11 Node checks.
  The focused physics, SQLite, and HTTP run passed 88 tests. Seven disposable
  Chromium phases passed on desktop and at iPhone dimensions. They covered a
  save written by the previous v4 host, v5 saves and recovery, the prediction
  limit, the SOI notice after reconnect, and body-center arrival.

## 2026-11-10 — Orbital reference frames and target states

- [Issue #27](https://github.com/Docbuchanan84/Quasar/issues/27) adds read-only
  orbit, station, and spacecraft targets. Existing orders and arrivals carry
  the body-center label.
- Eligible free coasts change primaries at bracketed Laplace SOI crossings.
  Position and velocity are preserved, and v5 checkpoints retain eligibility.
  Legacy free coasts retain their stored primary.
- `scripts/Verify.ps1` passed with 222 xUnit tests. Disposable Chromium verified
  the target endpoint, preview and explicit confirmation, arrival wording at
  desktop and phone widths, and save/restart. Physical iPhone testing remains open.

## 2026-11-10 — Continuous play through browser disconnects

- The host advances Continuous play through phone lock, tab closure, and lost
  connections. Stop from a connected browser or a game interrupt pauses it.
  Once still advances one selected interval, with interrupts able to end it early.
- The clock shows the interrupt message and simulation time, including on
  reconnect. Event history retains the event. Phone push delivery remains open.
- `scripts/Verify.ps1` passed with 211 xUnit cases and 11 Node checks. The
  disposable HTTP test ran for six seconds without browser requests and stopped
  the run using a different client and an older snapshot.
- An iPhone-sized Chromium session advanced exactly five seconds in Once,
  disconnected during Continuous play, then showed the exact burn cutoff at
  35 seconds after reconnect and reload. The broader campaign browser script
  stopped at an outdated course-form selector before reaching its arrival check.

## 2026-11-10 — Finite-thrust burns

- The first [issue #26](https://github.com/Docbuchanan84/Quasar/issues/26)
  slice computes a full-thrust segment under central gravity using Pathfinder's
  installed propulsion and decreasing mass. RK4 uses one-second steps anchored
  to ignition, with a partial final step through physical depletion.
- Timed cutoff and exhaustion interrupt both Once and Continuous processing.
  Manual cutoff preserves position and velocity, and the inspector reports
  current propellant. Checkpoint v4 restores the active burn and re-enqueues its
  completion boundary, addressing the missed restore event identified in PR #19.
- `scripts/Verify.ps1` passed with 215 xUnit tests. Disposable Chromium checks
  at 320 and 1440 CSS pixels covered ignition,
  mid-burn save and host restart, exhaustion at 150 seconds during a 30-day
  advance, manual cutoff, and an impulsive course after cutoff. Physical
  iPhone testing remains open.

## 2026-11-10 — Pathfinder propulsion foundation

- [Issue #25](https://github.com/Docbuchanan84/Quasar/issues/25) gives new
  Solar System Pathfinder ships one test chemical engine and tank. The
  inspector shows C#-derived mass, flow, acceleration, and theoretical delta-v.
- Checkpoint v3 stores the equipment and tank quantity. Versions 1 and 2
  restore without propulsion. Existing impulsive courses retain their flight
  and fuel-estimate behavior.
- `scripts/Verify.ps1` passed with 205 xUnit tests. Disposable Chromium
  checks covered 320 and 1440 CSS pixel widths, preview, order, advance,
  named save and reload, and v2 in-flight restore without equipment.
  Physical iPhone testing remains open.

## 2026-11-09 — System object tree

- Replaced the object grid with a collapsible tree based on the campaign's
  orbital hierarchy. Planet rows run from Mercury to Neptune by orbital size.
  Moons stay with their planets, stations appear under Constructed, and small
  bodies have their own branch. Pathfinder stays accessible when Sol is closed.
- Kept tree labels separate from saved object names and reused the existing
  map, inspector, and course selection path.
- `scripts/Verify.ps1` passed. A disposable Chromium session with iPhone
  emulation checked the hierarchy, tap and keyboard toggles, selection, course
  destination updates, and tree widths from 320 to 1024 CSS pixels. Physical
  iPhone validation remains open.

## 2026-11-09 — Course planning release and documentation

- Automatic course planning merged into `main` through
  [PR #21](https://github.com/Docbuchanan84/Quasar/pull/21). The published
  build and the merge use the same gameplay source.
- The private HTTPS preview served the updated client and restored the active
  campaign from the same checkpoint. A portrait browser check verified route
  selection and the maneuver budget control. The owner gave positive feedback.
- Reconciled the public gameplay, checkpoint, hosting, and validation guides
  with the current implementation. Historical results remain in the log.

## 2026-11-09 — Automatic course planning

- Plot Course accepts planets and moons and compares the fastest and lowest
  maneuver-cost routes found within the test budget. The selected preview
  determines the map path and the exact order committed.
- In one disposable-host run from the Solar System starting state, the Jupiter
  search took 2.45 seconds at 25 km/s and 1.02 seconds for the full no-route
  search at 1 km/s. The Moon search took 1.71 seconds at 25 km/s. These are
  local HTTP timings.
- Chromium checked Jupiter and the Moon, including the portrait layout and a
  Moon order in 3D. The automated checkpoint test reloads a committed Moon
  course. Physical iPhone verification remains open.


## 2026-11-09 — Solar System starter

New campaigns use the [Solar System catalogue and test course workflow](SOLAR_SYSTEM.md).
Both views share the authoritative body positions and orbit geometry. The Venus
assist regression checks exact encounter and arrival boundaries through checkpoint
reload. Format 1 Helios checkpoints remain readable.

Local verification passed 192 .NET tests, locked restore, formatting, renderer
checks, client tests, and the production build. The disposable API test passed
all nine intervals. Chromium checked the new campaign in desktop and iPhone 17
Pro emulation. Physical-device testing is still pending.


## 2026-11-08 — Private campaign release

- PR #17 merged as `f86ffd2` and passed main Verify. Published the merged
  build to the existing private Tailscale route. The prior release, deployment
  metadata, and read-only snapshots of the in-memory lab were retained privately.
- A disposable preflight created a paused campaign and confirmed the exact
  build ID. On the live route, the homepage, JS bundle, campaign status, and
  first-run screen loaded. The campaign library started empty and autosave
  was enabled. The Tailscale routing configuration was preserved.
- Physical iPhone Safari gameplay feedback is pending. See the
  [campaign workflow](CAMPAIGN_LIFECYCLE.md) and [rollback guide](HOSTING.md).

## 2026-11-08 — Campaign controls and restart verification

- [Issue #13](https://github.com/Docbuchanan84/Quasar/issues/13) adds New Game,
  Continue, retained manual saves, and checkpoint selection. The browser
  requires confirmation before replacing the active world, reports durable
  saves and recovery failures, and refreshes the authoritative map and
  controller state after a campaign switch.
- Disposable Chromium and SQLite regressions cover exact halfway restore,
  subsequent orders, cancellation, completed arrival, corrupt checkpoint
  recovery, and incompatible schema rejection. See the
  [campaign lifecycle](CAMPAIGN_LIFECYCLE.md) for commands and the
  [hosting guide](HOSTING.md) for release and rollback.
- Full verification passed 187 xUnit and 11 Node tests, formatting, lint,
  and the renderer/build checks. A disposable release publication preserved
  the hashes of all four save artifacts. Git ignores campaign databases,
  sidecars, and last-played markers.
- Physical iPhone campaign validation remained pending at implementation handoff.

## 2026-11-08 — Campaign lifecycle and crash recovery

- Implemented [issue #12](https://github.com/Docbuchanan84/Quasar/issues/12)
  from `72290c3` after PR #15 merged. The System Lab host restores the last-played
  campaign, creates separate campaigns, and supports manual saves, listing,
  explicit load, and Continue. A replacement always pauses and invalidates the
  previous session, preview plans, and controller lease.
- The default is one dirty recovery autosave per 300 seconds of real time.
  Captures are detached before disk I/O, and SQLite checks the observed head on
  every commit. Verified older checkpoints and independent backups support
  recovery. Failed recovery leaves an explicit error and the original data.
- `scripts/Verify.ps1` passed 186 xUnit tests, including 24 new lifecycle and HTTP
  tests, plus 11 Node tests, formatting, lint, and renderer/build checks.
  `scripts/Test-SystemLabApi.ps1` passed against a separate disposable server.
  Tests used temporary storage and included killing an isolated server process.
  Campaign validation ran entirely on disposable servers.
- Next is the [issue #13](https://github.com/Docbuchanan84/Quasar/issues/13)
  save/load UI using the [campaign API contract](CAMPAIGN_LIFECYCLE.md).

## 2026-11-08 — SQLite campaign checkpoint storage

- Implemented [issue #11](https://github.com/Docbuchanan84/Quasar/issues/11)
  on `codex/sqlite-save-storage` from `35b5b2c`. The host-side store persists
  the existing checkpoint bytes, validates SHA-256 and detached restore, and
  commits the checkpoint, sequence, and recovery rotation together.
- Named saves survive rotation. Recovery retains five checkpoints by default.
  Schema upgrades are forward-only. Verified SQLite backups include committed
  WAL data, retain prior copies, and restore under a new campaign ID so a
  damaged original remains available for inspection.
- `scripts/Verify.ps1` passed 162 xUnit cases, including 24 SQLite integration
  cases, plus 11 Node cases, format, lint, and production and renderer checks.
  An isolated run of `Publish-HostedApp.ps1` preserved the SHA-256 hashes of
  disposable campaign and backup files and included the native SQLite library.
- Next is [issue #12](https://github.com/Docbuchanan84/Quasar/issues/12). See
  [the storage API and lifecycle handoff](SQLITE_PERSISTENCE.md).

## 2026-11-08 — Versioned headless simulation state

- Implemented issue #10 on `codex/save-state-contracts`. The immutable version
  1 checkpoint retains catalogue definitions and epochs, spacecraft motion,
  solved conics and burns, exact pending boundaries, and ordered lab history.
  The UTF-8 codec writes Int64 values as decimal strings.
- Restore validates a detached world and rebuilds the live boundary queue. It
  preserves midflight and cancelled-coast physics and prevents consumed
  patches and arrivals from firing again. The private host and shared design
  record were outside the change.
- Verified with `scripts/Verify.ps1` (138 xUnit cases, including 39 checkpoint
  cases, plus 11 Node cases, format, lint, and production/renderer build checks).
- Next is [issue #11](https://github.com/Docbuchanan84/Quasar/issues/11) after
  merge. See [simulation state](SIMULATION_STATE.md) for the version 1 API and
  the SQLite checkpoint handoff.

## 2026-11-08 — Course release deployment and documentation

- PR #8 merged as `a3a052f` after passing CI and was published through the
  existing private preview. The release restart reset the in-memory lab. Prior
  releases and rollback metadata were retained. Private HTTPS returned the
  updated client, readiness, and a 65-point Iris course preview.
- Reconciled the player guide, simulation and HTTP contracts, hosting, milestone,
  test, and source records with the deployed code. The live Google Game Design
  Record now summarizes the current release near the top and keeps the earlier design and
  Task 002 evidence as historical context. The scoped native edit was verified
  by revision readback, headings, and links.
- Current course and 3D validation on the physical iPhone 17 Pro remains open.

## 2026-11-08 — Agent 4 course plotting and map integration

- Created isolated `codex/course-plotting` from navigation `a8190fe`; integration
  commit `6a09985` merges Agent 3 `afe1ca4` (including Agent 2 `2f79207`) while
  retaining all expanded time settings and newer gesture/camera behavior.
- Added the ship → Plot Course → planet selection → preview/adjust → Issue Order
  workflow. Preview cancellation is local. Explicit confirmation uses a bounded
  server token for the exact reviewed solved plan; stale/evicted/cross-instance
  tokens fail without movement. Existing direct API compatibility is preserved.
- Both maps display the same sampled paths, departure markers and future arrival
  markers with distinct preview/committed styling. The panel exposes physical
  data actually supplied by planning, approximate sampled distance, unavailable
  fuel and feasibility/model warnings. No client-side physical integration.
- Improved map reuse: event-driven Pixi redraw/retained symbols, linear hit scan,
  stable course references, Babylon object reconciliation and batched/cached DOM
  label projection. A 1,000-object synthetic check identified layout thrashing;
  fixing repeated layout reads reduced one two-button desktop automation sample
  from 2,271 ms to 326 ms. This is not a sustained performance/capacity claim.
- `scripts/Verify.ps1` passed: 99 xUnit cases, nine navigation and two coordinate
  Node cases, locked restore/build/format, frontend lint, renderer package/bundle
  checks and production build. The existing large lazy Babylon chunk warning
  remains. New tests cover preview isolation, exact commit, token lifetime and
  mapless long-jump arrival. Updated the older interval test for gravitational coast.
- Playwright CLI passed 22 course checks and 38 desktop/native Chromium
  touch navigation checks on isolated `:5094` / Vite `:5174`. Synthetic 1,000-object
  rendering/zoom/reset passed with zero planning calls or page exceptions. Desktop
  and 393 × 852 panel screenshots were visually reviewed; no horizontal overflow.
  Physical-iPhone/Safari and sustained large-world measurements remain open.
- Live **Quasar — Game Design Record** read verified on 8 November 2026; no shared
  document edit was requested/performed and write access was not tested. Updated
  README, decisions, plan, map/service contracts and [COURSE_PLOTTING.md](COURSE_PLOTTING.md).
- Owner subsequently authorized consolidation of Agents 1–5. Prerequisites are
  merged through integration PR #7 before this course branch is pushed against
  main. No force-push, private deployment, live-world reset, runtime dependency,
  system reconfiguration or private-source publication. Original OneDrive checkout
  remains on its navigation branch.
- Repeated browser remount/resize testing caught Pixi boolean-true cleanup releasing
  global batch pools still used by a replacement asynchronous initialization.
  Explicit app-local destruction preserves shared pools; a repeated StrictMode
  remount/resize regression now passes alongside all existing gesture checks.
- Next: owner physical iPhone 17 Pro validation of plotting/confirmation and both
  maps, with browser/OS versions. No new product/architecture decision is pending
  for the implemented scope; fuel/fleet/waypoint support remains future work.

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
