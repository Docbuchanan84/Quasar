# Development log

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
