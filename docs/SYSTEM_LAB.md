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

Run `scripts/Verify.ps1` for xUnit simulation/session tests and full repository
checks. Run `scripts/Test-SystemLabApi.ps1` only against an isolated stopped lab
(default loopback 5090); it mutates that lab's clock to test the real HTTP runner.
Browser smoke checks and screenshots live in ignored `output/playwright/`.

Physical iPhone validation remains an owner-assisted check: record iOS/browser
version; test portrait/landscape, gestures/selection/orders, Once/Continuous/Stop,
arrival, background/lock, Wi-Fi/cellular/reconnect, and graphics/battery behavior.
Do not infer these results from desktop viewport emulation.

Vite reports a large Babylon scene chunk (~1.09 MB uncompressed, ~265 KB gzip).
It is loaded only on selecting 3D. This is a known initial download cost, not
measured iPhone capacity. No elaborate art, external fonts/images, new runtime
packages, database, WebSocket transport, or permanent event scheduler is used.
