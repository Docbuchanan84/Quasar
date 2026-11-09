# System Lab

The playable private release includes the original Task 002 lab and the later
clock, celestial, movement, navigation, and course plotting work. Release
`a3a052f` was deployed on 8 November 2026. For operating instructions, see the
[README](../README.md) and [course plotting guide](COURSE_PLOTTING.md).

## Simulation and presentation

The headless C# fixture contains Helios, fixed planets Iris and Nereid, and
Pathfinder. Objects have stable IDs, positions in double-precision kilometres,
and velocities in kilometres per second. The system-local frame is right-handed.
Pixi shows XY and labels Z. Babylon shows XYZ with a Z-up camera. Both scale
coordinates by 1/1,000,000 and draw illustrative reference circles and enlarged
symbols. React shares selection, inspection, orders, and event history across
views. Rendering and camera controls operate on snapshots.

Pathfinder starts coasting under Helios gravity. The planner solves a Lambert
transfer from the current state to a selected fixture planet. A committed plan
applies impulsive departure and arrival changes with analytical coasting in
between. Arrival interrupts at the exact event tick, records an event, and
discards the unused advance time. Cancelling a committed course retains position
and velocity and starts an unforced coast. The reusable
[celestial service](CELESTIAL_MECHANICS.md) and
[trajectory engine](SPACECRAFT_TRAJECTORIES.md) also support moving targets and
explicit gravity patches. The player workflow exposes single-well transfers to
the fixed planets.

Select Pathfinder, choose **Plot Course**, select a planet, and review or adjust
the flight duration. **Issue Order** commits the exact reviewed plan while the
host instance and revision match. Previewing and cancelling a preview preserve
the simulation and any existing course. Both views draw the same sampled preview
or committed path. Departure and arrival markers use the planned positions.
Fuel estimates remain unavailable until propulsion and mass data exist.

## Clock and execution

Elapsed time uses nonnegative checked .NET ticks of 100 ns. The UI shows elapsed
days, hours, minutes, and seconds. Snapshot, trajectory sample, and exact
query timestamps use decimal strings across HTTP.

The nine supported intervals are 1, 5, 30, 60, 3,600, 86,400, 432,000,
1,296,000, and 2,592,000 seconds. Once processes one interval. Continuous uses
one interval per 250 ms host tick until Stop, an interrupt, clock overflow, or
expiry of the initiating browser's five-second lease. The visible client polls
every 500 ms and renews its run lease every second. A refreshed browser gets a
new controller ID. Another connected browser can inspect or Stop the shared run.

The persistence-enabled host restores the last valid checkpoint on restart.
First run requires creating a campaign through the [campaign API](CAMPAIGN_LIFECYCLE.md)
until the save/load UI is available. The currently deployed course release
holds its lab in memory.
**Reset View** changes the camera only.

## HTTP contract

| Request | Purpose |
| --- | --- |
| `GET /api/health` | Readiness and the `system-lab` stage |
| `GET /api/lab` | Instance/revision, run controls, objects and velocities, ship status, events, and any committed trajectory |
| `GET /api/lab/course-preview?destinationId=…&flightSeconds=…` | Solve a stopped-world course and return a revision-bound preview ID, sampled path, summary, and warnings |
| `GET /api/lab/trajectory-preview?destinationId=…&flightSeconds=…` | Compatibility query for an advisory trajectory without a confirmation ID |
| `GET /api/lab/celestial/{bodyId}/state?atTicks=…` | Read celestial state at a signed Int64 timestamp |
| `GET /api/lab/spacecraft/state?atTicks=…` | Read Pathfinder's current or predicted future state |
| `POST /api/lab/commands` | Apply a revision-checked command |
| `POST /api/lab/lease` | Renew the active controller lease using `instanceId` and `clientId` |

Commands take JSON `{instanceId, revision, clientId, operation}` plus operation
arguments. The current UI uses `configure` with `durationSeconds` and `mode`
(`once` or `continuous`), `play`, `stop`, `commit-course` with `previewId`,
and `cancel-travel`. The compatible direct `travel` operation accepts
`destinationId` and optional `flightSeconds`, defaulting to 21,600. The UI
requires course review before issuing an order.

The host retains up to 32 solved preview plans. A successful command or clock
tick invalidates them. The session gate serializes reads and writes. The server
returns 409 for stale instances, stale revisions, or expired course previews, and
400 for invalid command arguments. Stop accepts an older revision for the same
instance. POST requests require JSON and an Origin authority matching the Host.
API responses use `Cache-Control: no-store`. On uncertain command outcomes,
the browser refreshes state without automatically replaying the command.

## Verification and open work

The merged release passed 99 xUnit and 11 Node cases, 22 Chromium course checks,
38 navigation/lifecycle checks, and isolated HTTP suites. Run
`scripts/Verify.ps1` for the repository suite. Run `scripts/Test-SystemLabApi.ps1`
against an isolated stopped lab because the script advances time. Live deployment
verification covered private HTTPS, the new client, and the course preview
endpoint. See [course plotting](COURSE_PLOTTING.md) for browser test reproduction.

Initial physical-phone portrait 2D play is confirmed. The current course workflow,
physical-phone 3D, lifecycle behavior, and measured performance remain open.
Player-facing save controls, finite thrust and propellant use, collision and docking, fleets,
and wider simulation domains are future work. See the [project plan](PROJECT_PLAN.md).
