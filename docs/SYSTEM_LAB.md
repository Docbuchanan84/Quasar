# System Lab implementation — Task 002

## Authority and state

`Quasar.Simulation/SystemLab.cs` contains the reusable predefined fixture, immutable
value records, travel rule, and explicit elapsed-time advance. It has no host,
browser, storage, timer, or rendering dependency. Fixture IDs are fixed strings;
the server supplies a different instance ID after each restart.

Coordinates are double-precision kilometres in a right-handed system-local frame.
Pixi projects X right/Y up and shows Z in labels; Babylon uses the same XYZ frame
and an explicit right-handed scene. Rendering scales kilometres by 1/1,000,000.
Reference circles pass through each stationary planet in an inclined plane; their
XY projection agrees across views. They are illustrative, not simulated orbits.
Symbols and labels have display sizes independent of physical body size.

Time uses nonnegative checked .NET ticks (100 ns) up to TimeSpan.MaxValue. The UI
shows elapsed days/hours/minutes/seconds, not a universal calendar. JSON numbers
are display approximations at very large tick values; C# retains the integer clock.
Same-build equivalent advance sequences produce identical fixture coordinates;
distance assertions use a 0.000001 km tolerance. No cross-platform replay promise.

Travel derives position from the stored departure position/time and the absolute
clock. Duration is rounded upward to the next tick. Arrival snaps to the destination
and stops at that tick, recording one event and discarding any requested remainder.
Retargeting begins from the current canonical position. Orders to non-planets,
the active destination, and an already-occupied destination are rejected atomically.

## HTTP and execution

- `GET /api/health`: readiness and `stage: system-lab`.
- `GET /api/lab`: instance/revision, run state/reason, interval/mode, world objects,
  ship status/ETA/distance, and chronological timestamped events. No-store.
- `POST /api/lab/commands`: JSON `{instanceId, revision, clientId, operation}`.
  Operations: `configure` (durationSeconds, mode), `play`, `stop`, `travel`
  (destinationId). Durations: 1/60/3600/86400/2592000; modes: once/continuous.
- `POST /api/lab/lease`: JSON `{instanceId, clientId}`; renews only the current
  controller's active run. No world/revision mutation.

The session lock serializes snapshots, commands, and runner ticks. An instance or
revision mismatch returns 409; invalid commands return 400. Stop alone accepts an
older revision in the same instance so ticks cannot defeat it. Revisions ahead
of the server and other instances remain invalid. Commands require JSON and an
Origin authority matching the request Host; no CORS, public access, or new login
service was introduced. Origin checking complements the existing private network
boundary; it is not per-user authentication.

A 250 ms host timer processes one analytically evaluated interval per tick while
running. Once stops after one interval. Continuous repeats until Stop, arrival,
clock overflow, or the controller's five-second lease expires. It is accelerated
interval processing, not one simulated second per real second. The browser polls
at 500 ms while visible and renews a run lease every second. Polling and rendering
never move the simulation. Unknown command outcomes are refreshed and never replayed.

A reload retains world state but creates a new browser ID; an existing run stops
when its previous lease expires. Another browser can inspect or Stop. This is a
single shared lab, not separate per-browser games. No save/load or reset endpoint.
All history is retained for the process lifetime; data volume/retention awaits a
future persistence task. Long-idle and hidden renderer work is suppressed; only
one lazily loaded renderer is mounted at a time.

## Verification and limits

**Implementation and initial owner acceptance: 8 November 2026.**
The working vertical slice is deployed; it is no longer an empty skeleton.

| Area | Evidence/result |
| --- | --- |
| Headless simulation and session | 21 xUnit cases passed: fixture/IDs/XYZ, clock boundaries, movement, retargeting, exact arrival, equivalent increments, invalid commands, snapshot isolation, execution/lease/concurrency |
| Full repository check | `scripts/Verify.ps1` passed build, tests, format, frontend lint/type/build, and renderer loading/bundling |
| HTTP boundary | `scripts/Test-SystemLabApi.ps1` passed on isolated loopback and deployed private HTTPS |
| Desktop/browser workflow | Real Pixi/Babylon rendering, selection, shared inspector XYZ, orders, movement/arrival, time controls, refresh, offline/reconnect, camera controls, view disposal, failed-map recovery |
| Responsive layout | Automated desktop, 402/320 px portrait and landscape checks; supplied actual-phone portrait 2D screenshots |
| Initial phone experience | Owner confirmed the look/interaction works as intended; screenshots show selected ship, movement/depth changes and stopped/processing Continuous states |
| Performance experiment | 100,000 idle 30-day advances measured at 6.366 ms locally in Release; not a scale guarantee |
| Publication | Implementation `32d45a8`, PR #2, passing implementation CI; private deployment and unchanged Tailscale routes verified |

The public [mobile validation record](MOBILE_AND_REMOTE_ACCESS.md) describes the
screenshots without exposing private browser/address content. Neither the screenshots
nor owner acceptance proves every gesture, 3D GPU capability, or sustained performance.

Run `scripts/Verify.ps1` for xUnit simulation/session tests and full repository
checks. Run `scripts/Test-SystemLabApi.ps1` only against an isolated stopped lab
(default loopback 5090); it mutates that lab's clock to test the real HTTP runner.
Browser smoke checks and screenshots live in ignored `output/playwright/`.

Basic actual-phone portrait 2D use is now confirmed. Still record iOS/browser
versions and explicitly test physical-phone 3D, full gestures/order/arrival flow,
landscape, background/lock, cellular-only access/reconnect, and graphics/battery
behavior. Desktop emulation and the supplied still images do not complete these checks.

Vite reports a large Babylon scene chunk (~1.09 MB uncompressed, ~265 KB gzip).
It is loaded only on selecting 3D. This is a known initial download cost, not
measured iPhone capacity. No elaborate art, external fonts/images, new runtime
packages, database, WebSocket transport, or permanent event scheduler is used.

## Owner operating guide and reset

Select Pathfinder on a map or in the object list, inspect its XYZ/status, and choose
a planet travel order. Choose an interval and Once/Continuous, then Play. Once
processes one interval; Continuous repeats it until Stop, arrival, lease expiry,
or the clock limit. Stop before changing controls or issuing/replacing an order.
After an arrival interrupt, review the event and issue another order or press Play
to continue with a fresh selected interval; unused advance time is not carried over.

Views share selection and state; camera/view/reference settings are presentation.
Inspectors and history can collapse on a phone. The planetary inset is illustrative.
State is shared between connected browsers, not a separate world per tab/device.

For an explicitly requested reset, use the existing Stop/Start hosted scripts
without republishing; a restart restores the fixture. The owner requested this
after deployment, and the reset was verified at elapsed zero, paused, ship idle,
four objects, and one initialization event. A page refresh does not reset the lab.
There is no in-app reset command, save/load, undo, or automatic recovery of play
progress. Do not restart a live session merely to refresh documentation.

## Completion boundaries and next task

The owner accepted the initial System Lab experience. This does not promote
stationary bodies, fixed 25 km/s travel, analytic point motion, runner cadence,
lease policy, illustrative orbit guides, or surface imagery into permanent game
rules. Gravity, acceleration, collision/docking, orbital dynamics, persistent
saves, terrain, and additional game domains remain outside Task 002.

The single recommended next development task is versioned save/load with atomic
writes and recoverable backups. It needs its own concrete implementation scope;
no database, format, migration policy, or permanent scheduler is selected here.
