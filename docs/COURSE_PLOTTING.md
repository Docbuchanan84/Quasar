# Course plotting and map integration

Select Pathfinder and open **Plot Course** while simulation time is stopped.
Choose a planet or moon from either map, the object list, or the destination
picker. Choose **Velocity-matched approach** or **Orbit preset**. Quasar
searches from the current departure state and shows the executable
**Fastest** route and the **Lowest maneuver cost** route it finds. The fastest
route is selected initially. If both criteria pick the same trajectory, the
panel shows a single option. Select a route to see it on the map, then use
**Issue Order** to commit it or **Cancel preview** to close the panel.

The course panel shows the arrival condition and error, burn start times and
durations, expected propellant spent, and the propellant reserve after braking.
Reserve is shown in kilograms and as a percentage of departure propellant.
Below 1%, the option and confirmation panel warn about the small margin for
later maneuvers. The warning leaves the decision to the player.
**Advanced** applies an additional delta-v limit. The installed engine and
propellant inventory always constrain the search. Infeasible results identify
fuel, thrust, geometry, or arrival conditions that prevented a course.

The planner checks 40 whole-second durations over a range of one second to ten
Julian years, then checks additional times around the earliest feasible and
lowest-cost results. It refines direct Lambert arcs into two fixed-direction
burns and one analytical coast in the current gravity primary. The approach
endpoint lies at 110% of the target's Laplace SOI on the outward radial, with
the velocity of that moving point. Earth and Moon orbit presets require the
ship to be in the destination body's primary already. The search can miss narrow
windows. Multi-primary capture and finite flybys remain unsupported. See
[trajectory limits](SPACECRAFT_TRAJECTORIES.md#propulsion-aware-courses).

## Exact confirmation and stale state

`GET /api/lab/course-preview?mode=auto&destinationId=...` returns a
`fastest` preview and an optional distinct `lowestManeuverCost` preview. Pass
`arrivalKind=approach` (default) or `arrivalKind=orbit`.
Both previews are null when no candidate satisfies the search, with details in
`infeasibleReasons`. Each preview carries an
opaque `previewId`, host instance and revision, ship and destination IDs,
warnings, trajectory samples, burn schedule, and arrival conditions. An optional
`maneuverBudget` sets an additional limit in km/s. Fixed-duration callers can
continue using `flightSeconds` without `mode=auto` for a legacy impulsive
calculation. For a spacecraft with modeled propulsion the result is advisory
and `previewId` is null.
Automatic search uses captured departure state outside the session lock and
checks that the session still matches before registering tokens.

The sampled-distance figure sums consecutive server-supplied sample chords; it
is a rendering approximation, not an analytical physical path length.

The host retains at most 32 immutable solved plans as disposable confirmation
tokens. Preview reads change no canonical position, velocity, clock, revision,
orders or history. Discarding a preview needs no server command. Tokens are
instance-bound and are invalidated by any successful command or clock tick.
An expired/evicted token receives HTTP 409 and requires recalculation/review.

`commit-course` on the existing same-origin JSON command endpoint accepts only
`previewId` in addition to the normal instance/revision/client envelope. It commits
the retained **solved plan**, never browser geometry or a newly solved plan from
potentially changed inputs. Execution also validates the departure state, time,
catalogue and installed propulsion. A successful commit consumes all outstanding previews.
Direct `travel` returns a conflict for ships with modeled propulsion. Previously
saved impulsive courses still load and execute, and ships from campaigns without
modeled propulsion retain the legacy order API. Use `cancel-travel` to cancel a
committed course.

The React hook keys results by host instance/revision, ship, destination, maneuver
budget and explicit retry. Old overlays and confirmation eligibility disappear
immediately on a key change. Cleanup aborts in-flight requests; late results are
ignored. Polling/reconnect refreshes state. Host restart discards plotting state.
No uncertain order is automatically replayed; the plotting panel closes after a
confirmation attempt so another attempt starts with a new review.

## Shared visualization and performance

React shares the same committed and preview sample arrays with either lazy map.
Both use Agent 2's system XYZ kilometres and the existing shared map scale.
Pixi projects XY; Babylon preserves XYZ. Neither integrates gravity, extrapolates
ship motion, advances time or sends a command from its camera/render loop.

- Preview: gold dashed path; committed: green solid path. A replacement preview
  can coexist with the committed course until explicit confirmation.
- Departure markers use squares/boxes. Arrival/future-destination markers use
  outlined diamonds at the final authoritative trajectory sample, rather than
  the destination's present position. Markers are unpickable in 3D, preserving
  physical-object selection. The map legend explains the shapes and colors.
- The full departure-to-arrival path remains fixed while the ship symbol follows
  authoritative snapshots. Arrival removes the completed path via server state.
  Switching/closing maps does not change execution or the retained plan.
- Solving/sampling is server work on meaningful requests/commit, never redraw.
  Polling preserves an unchanged committed path reference after one comparison
  per new snapshot; Babylon rebuilds course meshes only when shared layers change.
- Pixi retains symbols/text, redraws on state/input/resize/resume and performs no
  periodic serialization or idle redraw. Hit testing scans once without sorting.
- Babylon reconciles object meshes on snapshot changes, caches unchanged label
  projections, and reads viewport layout before writing label styles. Camera
  operations remain local. Existing lazy loading, hidden-tab pause and disposal
  behavior remain. App-local Pixi disposal explicitly preserves global pools so
  late StrictMode initialization cleanup cannot invalidate an active replacement.

## Verification

`scripts/Verify.ps1` covers the headless search, exact selected-route commit,
and finite-course checkpoint reload. The browser scenario exercises an
infeasible Jupiter route, unsupported Earth capture, default and alternate Earth
approach options, 2D/3D review, portrait layout, and exact order confirmation.

Run the browser scenario against an isolated same-origin host serving the built
client. Set a disposable persistence data root before starting the host, and
create a fresh Solar System campaign. From the repo root:

```powershell
New-Item -ItemType Directory -Force output/playwright
npx --yes --package @playwright/cli@0.1.23 playwright-cli -s=quasar-course open $disposableAddress
npx --yes --package @playwright/cli@0.1.23 playwright-cli -s=quasar-course run-code --filename src/Quasar.Client/scripts/check-course-browser.js
npx --yes --package @playwright/cli@0.1.23 playwright-cli -s=quasar-course close
```

The scenario commits an Earth approach. Use a fresh disposable campaign for each
run. See [mobile validation](MOBILE_AND_REMOTE_ACCESS.md) for hosted portrait
checks and physical-device coverage.
