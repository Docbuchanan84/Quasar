# System Lab

The System Lab is Quasar's playable single-system sandbox. For
operating instructions, see the [README](../README.md) and
[course plotting guide](COURSE_PLOTTING.md).

## Simulation and presentation

New campaigns use the [Solar System catalogue](SOLAR_SYSTEM.md), fictional
Earth installations, and Pathfinder. The original Helios fixture remains
available for headless tests and older checkpoints. Objects have stable IDs,
positions in double-precision kilometres, and velocities in kilometres per
second. The system-local frame is right-handed.
Pixi shows XY and labels Z. Babylon shows XYZ with a Z-up camera. Both scale
coordinates by 1/1,000,000 and draw illustrative reference circles and enlarged
symbols. React shares selection, inspection, orders, and event history across
views. Rendering and camera controls operate on snapshots.

Pathfinder starts coasting under stellar gravity. Automatic planning refines
Lambert transfers to the requested arrival condition with a finite departure
burn, analytical coast, and finite braking burn. Arrival interrupts at the
exact event tick, records an event, and
discards the unused advance time. Cancelling a committed course retains position
and velocity and starts an unforced coast. The reusable
[celestial service](CELESTIAL_MECHANICS.md) and
[trajectory engine](SPACECRAFT_TRAJECTORIES.md) also support moving targets and
explicit gravity patches.

Select Pathfinder, choose **Plot Course**, select any modeled body or station, and review
the fastest and lowest maneuver-cost routes found. **Issue Order** commits the
exact reviewed plan while the host instance and revision match. Previewing or
cancelling a preview preserves the simulation and any existing course. Both
views draw the same sampled preview or committed path. Departure and arrival
markers use the planned positions.
Automatic courses spend propellant while burning. A manual
[engine burn](SOLAR_SYSTEM.md#pathfinder-propulsion) requires a free coast.
Legacy impulsive test orders report `arrivalKind: "body-center"` and leave
propellant unchanged.

The optional [Sundiver sail trial](SOLAR_SAIL.md) adds a second spacecraft
with timed solar sail trim, a coast comparison, and a prepared Earth flyby.

Select any spacecraft to see its cyan **Flight path** in either map. The
line starts at the ship's current position and follows its flight orders
and gravity, including predicted gravity handoffs. It looks up to 180
simulated days ahead, ends at a committed arrival, and uses a shorter window
for short local orbits. The map's **Flight path** checkbox controls visibility.

## Clock and execution

Elapsed time uses nonnegative checked .NET ticks of 100 ns. The UI shows elapsed
days, hours, minutes, and seconds. Snapshot, trajectory sample, and exact
query timestamps use decimal strings across HTTP.

The nine supported intervals are 1, 5, 30, 60, 3,600, 86,400, 432,000,
1,296,000, and 2,592,000 seconds. Once processes one interval. Continuous uses
one interval per 250 ms host tick until Stop, a ship arrival, or the clock
limit. Locking the phone, closing the tab, or losing the browser connection
does not stop the host. The visible client polls every 500 ms and refreshes on
return. Any connected browser can inspect or Stop the shared run.

An arrival pauses at its exact tick, including during Once. The host includes
it in `interrupt`. Gravity changes, burn boundaries, and propellant exhaustion
are recorded at their exact ticks while the clock completes the remaining
interval. The clock displays the latest such event in `flightNotice`. Both
fields are available on reconnect and clear on Play or campaign replacement.
All events remain in campaign history.

The host restores the last valid checkpoint paused on restart. Use **New Game**
on first run, or use **Load Game** and **Continue** to restore a campaign. See
the [campaign workflow](CAMPAIGN_LIFECYCLE.md). **Reset View** changes only
the camera.

## Arrival notifications

Open Quasar from its iPhone Home Screen icon, select **Enable arrival
notifications**, and allow the iOS prompt. The same control disables delivery
for that device. The alert names the destination and says that the game
paused. Tapping it opens Quasar over the private HTTPS route.

New completed approaches, orbital insertions, and legacy arrivals enqueue an
alert after the host pauses. Other flight events and restored arrivals
appear in-game. The sender runs separately from the
clock and submits encrypted Web Push to Apple. Alerts expire after one hour.
The browser can be closed while the Quasar host is running.

## HTTP contract

| Request | Purpose |
| --- | --- |
| `GET /api/health` | Readiness and the `system-lab` stage |
| `GET /api/lab` | Instance/revision, run controls, arrival interrupt and latest flight notice, objects and velocities, ship status, events, and any committed trajectory |
| `GET /api/lab/course-preview?mode=auto&destinationId=…&arrivalKind=…` | Search a stopped-world departure for executable courses to an approach or orbit and return revision-bound previews or infeasibility reasons |
| `GET /api/lab/course-preview?destinationId=…&flightSeconds=…` | Solve a single fixed-duration course for existing callers |
| `GET /api/lab/sail-preview?angleDegrees=…&durationSeconds=…` | Compare a timed sail trim followed by coast with a ballistic coast and obtain a confirmation token. Omit both parameters for a coast preview. |
| `GET /api/lab/trajectory-preview?destinationId=…&flightSeconds=…` | Compatibility query for an advisory trajectory without a confirmation ID |
| `GET /api/lab/celestial/{bodyId}/state?atTicks=…` | Read celestial state at a signed Int64 timestamp |
| `GET /api/lab/target-state?kind=…&targetId=…&atTicks=…` | Read inertial target position and velocity and the reference body. Orbit presets use `planet-earth` at 400 km or `moon-moon` at 100 km. Station and spacecraft kinds use object IDs. |
| `GET /api/lab/spacecraft/state?atTicks=…` | Read Pathfinder's current or predicted future state |
| `GET /api/lab/spacecraft/{shipId}/flight-path` | Sample the spacecraft's current burn, trim, committed course, rendezvous or coast without advancing time |
| `POST /api/lab/commands` | Apply a revision-checked command |

The two spacecraft state queries accept automatic coast predictions through
30 days inclusive. A later tick returns HTTP 400 with the message
“Automatic coast predictions cover up to 30 days.” For an active burn, the
post-burn prediction window starts at its scheduled cutoff. The limit does
not apply to independent orbit and station targets.

Commands take JSON `{instanceId, revision, clientId, operation}` plus operation
arguments. The current UI uses `configure` with `durationSeconds` and `mode`
(`once` or `continuous`), `play`, `stop`, `commit-course` with `previewId`,
and `cancel-travel`. The legacy direct `travel` operation accepts `destinationId`
and optional `flightSeconds`, defaulting to 21,600, for ships without modeled
propulsion. Equipped ships require an automatic course preview. While paused, use `start-burn`
with `burnDirection` (`prograde` or `retrograde`) and `burnSeconds` (1–3,600),
or `cut-thrust` with no extra arguments. `world.ship.burnEndTicks` is the
scheduled cutoff as a decimal tick string during an active burn.

`fit-exploration-torch` equips and refuels the test ship while paused and freely
coasting. The operation preserves position, velocity and time and is recorded
in campaign history. See the [torch fixture](SOLAR_SYSTEM.md#pathfinder-propulsion).

`debug-refuel` takes the selected `shipId` and fills its installed tank while
paused and freely coasting. It keeps the ship’s equipment. The
[Torch Designer](TORCH_DESIGNER.md) adds a separate campaign refit that keeps
the tank and its current reaction mass.

While paused, `launch-sailcraft` adds Sundiver once per Solar System campaign.
`start-sail-trim` takes its `shipId` and the reviewed `previewId`.
`feather-sail` takes its `shipId` and ends an active trim at the current tick.

The host retains up to 32 solved preview plans. A successful command or clock
tick invalidates them. Automatic searches use a captured departure outside the
session gate and recheck the instance and revision before issuing previews. The server
returns 409 for stale instances, stale revisions, or expired course previews, and
400 for invalid command arguments. Stop accepts an older revision for the same
instance. POST requests require JSON and an Origin authority matching the Host.
API responses use `Cache-Control: no-store`. On uncertain command outcomes,
the browser refreshes state without automatically replaying the command.

## Verification and open work

Run `scripts/Verify.ps1` for simulation, session, and client checks. Run
`scripts/Test-SystemLabApi.ps1`
against an isolated stopped lab because the script advances time. Live deployment
verification covers HTTPS, the browser assets, campaign restore, and read-only
course previews. See [course plotting](COURSE_PLOTTING.md) for browser test reproduction.

Initial phone play and owner feedback are recorded in
[mobile validation](MOBILE_AND_REMOTE_ACCESS.md). End-to-end course and campaign
checks on the physical phone, 3D, lifecycle behavior, and measured performance
remain open. Collision, docking, and player-facing fleet controls remain
future work. See the [project plan](PROJECT_PLAN.md).
