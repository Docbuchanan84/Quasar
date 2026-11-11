# Spacecraft trajectories and movement

The player selects a destination and reviews the routes returned by automatic
course planning. The host retains the exact preview for revision-bound
confirmation. See [course plotting](COURSE_PLOTTING.md) for the UI and host API.

Implemented for the owner's Agent 3 request on 8 November 2026, stacked on
Agent 2's celestial service (`2f79207`). No runtime packages, fleet hierarchy,
propulsion model, save format or permanent general scheduler were added.

## Ownership and interfaces

| Type | Responsibility |
| --- | --- |
| `TrajectoryPlanner` | Pure preview solving, feasibility checks and bounded reuse |
| `TransferRequest` / `TransferLeg` | Initial position/velocity, epochs, primary, target, arc, clearance and optional delta-v budget |
| `TrajectoryDestination` | Celestial future ephemeris or fixed/constant-velocity inertial waypoint |
| `TrajectoryPlan` | Immutable solved segments, burns, delta-v, travel time and fuel availability |
| `TwoBodyTrajectory` | Time-parameterized, primary-relative universal Kepler propagation |
| `MovementExecution` | Authoritative clock, committed plans, coasts, rendezvous and ordered events for multiple ships |
| `Sample` / `SystemLab.Present` | Disposable timestamped positions/velocities for maps |
| `ITransferFuelEstimator` | Future estimate adapter; never spends inventory |

Agent 2's inertial XYZ frame and units are retained: km, km/s, GM in km³/s²,
signed Int64 ticks (100 ns). No render frame or screen coordinate enters physics.
Execution requires one serialized writer. The host locks commands and movement
queries. A future query forecasts the current order without advancing time or
processing events. Execution rejects historical queries before its current clock;
retained plans remain queryable within their own intervals.

## Physical method

Resolve the destination at the specified **arrival** epoch. Subtract the primary
state and solve the zero-revolution Lambert boundary problem with universal
variables and Stumpff functions. The resulting coast obeys the point-mass gravity
equation `acceleration = -GM r / |r|³`; it is not an interpolated chord. Short and
long arcs are selectable. Departure is an instantaneous velocity change, coast is
an unforced conic, and arrival instantaneously matches the target velocity.
Delta-v sums the magnitudes of all burns. Each individual solve takes a flight
duration. Automatic plotting compares solved candidates across sampled
durations and presents the best options found.

Propagation uses a bounded, safeguarded Newton solution of universal Kepler's
equation and Lagrange f/g coefficients. Elliptic phase is reduced before converting
ticks to double. Elliptic, near-parabolic and hyperbolic coasts are supported.
Every solve verifies its propagated endpoint against the target within
`max(0.00001 km, relative endpoint radius * 1e-8)`. At exact arrival the target
state removes numerical residue. At impulses, queries return post-burn velocity;
burn vectors retain the velocity discontinuity.

`PlanPatched` accepts explicit legs, primaries and patch epochs. A primary switch
must be parent/child and within 1% of the child's Agent 2 Laplace SOI radius.
Position is continuous; required velocity corrections are explicit, costed burns.
The primary ephemeris translates each conic into system XYZ. This is a simplified
patched-conic approximation, not n-body gravity. There is no automatic crossing
search or waypoint optimization. A single leg uses only its selected well and
does not model a third body's encounter forces.

Physical references:
[NASA, A Universal Solution of Lambert's Problem](https://ntrs.nasa.gov/citations/19700027262),
[NASA/JPL, Universal Lambert and Kepler algorithms for autonomous rendezvous](https://ntrs.nasa.gov/citations/19900065903).
The bounded implementation here is independent and does not reproduce all the
capabilities of those publications.

## Execution, cancellation and time

```csharp
var planner = new TrajectoryPlanner(celestialCatalogue);
var movement = new MovementExecution(celestialCatalogue);
movement.AddSpacecraft("ship", initialState, "star");
var preview = planner.Plan(new(movement.View("ship").State, movement.ElapsedTicks,
    new("star", arrivalTicks, new("planet")), MaximumDeltaVKmPerSecond: 30));
var points = preview.Sample(); // no commit or state mutation
movement.Commit("ship", preview);
var events = movement.Advance(TimeSpan.FromDays(30));
// Inspect consequences before resuming. Cancel only while a course is committed.
```

Commit requires the same catalogue and exact departure time/state. It stores
solved conics, not display points. Replanning starts from current position **and
velocity**. Cancellation preserves momentum and starts an unforced coast under
the current segment's primary; it does not stop the ship in space.

Advancement stops at the first patch/arrival and handles all simultaneous events
in stable ship-ID order. Obsolete events are discarded and periodically compacted.
Negative durations and time overflow fail before clock mutation. No map or timer
is required. Pause means no advancement; acceleration supplies larger/more
advances. The host Once/Continuous/Stop controls remain authoritative.

After arrival an abstract rendezvous attachment follows the target ephemeris.
This is **not** a docking, landing, capture-orbit or stationkeeping model. It
incurs no fictional fuel expenditure. A subsequent order departs that state.

## Host and presentation

- `GET /api/lab/trajectory-preview?destinationId=planet-iris&flightSeconds=21600`
  returns a summary and samples, without changing revision, orders, clock or events.
- `GET /api/lab/spacecraft/state?atTicks=...` returns a current/future authoritative
  position and velocity under the current orders without processing events.
- `POST /api/lab/commands`: legacy `travel` for ships without modeled propulsion takes `destinationId` and optional
  `flightSeconds` (default six hours); `cancel-travel` cancels. Both require stopped
  time processing. Existing revision/instance/client and same-origin JSON checks
  remain. Preview is advisory; changed departure state/time needs a fresh preview.
- New trajectory tick fields are decimal **strings** preserving Int64 in browsers.
  The pre-existing numeric lab clock/event contract is unchanged by this task.
- Snapshots expose current ship velocity and the committed trajectory. Both maps
  draw its sampled curve instead of a line to the current destination. Sampling
  occurs once on lab commit; snapshot reads copy a small array without resampling.
  `Sample` supplies 2–1025 uniform samples plus every patch epoch.

The lab retains its fixed planets as an artificial fixture. Pathfinder now starts
on a circular Helios-relative coast. The old 25 km/s value was not a propulsion
capability and is removed. The course UI and API default to a six-hour transfer
and accept other supported durations. Generic tests cover moving orbital/inertial targets and primaries.

## Feasibility, fuel and limits

Errors identify invalid/nonfinite state, unknown bodies, nonpositive GM, invalid
epochs, collinear endpoints, radial/collision conics, numerical range/convergence
failures, clearance violations and insufficient delta-v budget. Clearance checks
the **whole conic** periapsis conservatively; it can reject a safe finite arc.
Callers must supply meaningful clearance. Default 1 km is numerical protection,
not a stellar/planetary radius. There is no body-radius/terrain collision model.

Each leg permits up to 100 Julian years, with at most 32 explicit patched legs.
The lab input permits one second to ten Julian years. Unsupported extremes fail
without falling back to straight lines. Automatic launch windows/SOI detection,
multi-revolution or collinear transfers, finite thrust, n-body perturbations,
encounter prediction, docking and stationkeeping remain outside this task.

Catalogue replacement creates a new planner and invalidates old previews. A
caller introducing changed definitions must rebuild/replan affected execution
state explicitly. No mutable definition command or automatic midflight catalogue
replacement exists in the lab. Changed state/time/target/arc/clearance/budget
changes the planner cache key.

The legacy impulsive planner reports required delta-v and an unavailable route fuel estimate.
The [Pathfinder propulsion model](SOLAR_SYSTEM.md#pathfinder-propulsion) reports
separate equipment-derived performance. Impulsive course execution does not debit
its tank.

## Orbital destination states and automatic coast transitions

Body-local XYZ has parallel, nonrotating axes. At a shared tick, subtract or
add the reference body's inertial position and velocity together. Orbit targets
use a bound two-body conic above the reference surface and within the modeled
SOI. Station targets use their ephemerides. Spacecraft targets sample current
orders and freeze the resulting state at the requested tick for advisory use.

Automatic transitions apply to eligible free coasts. Entry uses 99% of the
child's Laplace SOI and exit uses 101% of the current body's SOI. The crossing
must clear the nominal boundary by 1 cm to keep grazing contacts stable.
The search uses a bound on relative acceleration to rule out whole intervals
and subdivides possible crossings down to the first 100 ns clock tick on the
new side. This detects fly-throughs whose endpoints are both outside and
avoids scanning the full time span at a fixed cadence.

The first crossing interrupts advancement and seeds the next conic with the
same inertial position and velocity. Earliest time wins in overlapping
spheres. At the same tick, exit takes precedence over entry, child IDs break
entry ties, and simultaneous events across ships are processed together.
Resume after each interrupt to reach the original requested end time.

The scanner works in windows of up to 30 days. Read-only predictions allow
30 days inclusive from the current tick for an eligible coast, or from the
scheduled cutoff for its predicted post-burn coast. A prediction beyond the
window returns an explicit error. Orbit presets and station ephemerides are
independent of that limit. Advancement can continue beyond the prediction
window. The model still uses patched two-body dynamics, keeps the prescribed
primary during committed transfers and powered flight, and skips handoffs
that would require unsupported radial/collision conics.

Body-center arrival uses the legacy velocity-matching impulse. Resolving a
target position and velocity supplies neither a capture maneuver nor contact
dynamics. Orbital capture requires a reachable approach, braking and a
verified bound post-maneuver state. Docking and landing need separate contracts.

## Finite burns

The powered segment integrates central gravity and fixed-direction thrust in
primary-relative km and seconds. RK4 uses one-second steps from the ignition
epoch. Mass decreases at the rated flow through physical cutoff, with a
partial final step when required. Exhaustion interrupts at the first 100 ns
clock tick at or after depletion. The handoff takes the powered endpoint's
position and velocity as the initial conditions of the analytical coast.
Queries and clock partitions produce identical results within a single build
because they evaluate from the same ignition data rather than chaining steps
across caller advances.

For the tested 150-second full-tank case at 150 million km under solar gravity,
the endpoint is within 1 mm and 0.01 mm/s of an independent 0.01-second RK4
reference. These tolerances cover that fixture. Close approaches, primary
switching during burns, and general error guarantees require further modeling.

## Propulsion-aware courses

Automatic courses start from direct zero-revolution Lambert guesses. The
rocket equation converts the departure and braking vectors into durations
using the installed thrust, exhaust velocity, mass flow, and changing wet
mass. Each automatic-course burn is limited to one day. Manual ignition remains
limited to one hour. Up to 16 iterations propagate the
departure burn forward and the desired arrival state backward through the
braking burn, then update both burn vectors from a new Lambert coast.

The accepted path is rebuilt entirely forward with the powered-flight RK4
integrator. It must retain propellant after braking and reach the requested
position within 10 m and velocity within 1 mm/s in the model. Orbit insertion
also requires an achieved bound orbit above the surface and inside the SOI.
Clearance checks use whole-conic periapsis, bounded coast SOI checks, and a
continuous enclosure on powered legs. Each RK4 step is enclosed around its
starting tangent by half the bounded acceleration times the step duration
squared. The ship bound uses half the starting radius and maximum dry-mass
thrust. The child frame adds the child's maximum acceleration at periapsis.
If the enclosure could touch a surface or sphere boundary, the course is
rejected. This can reject very close safe passes.

Cross-primary courses refine against the full patched coast, including moving
SOI entries and exits, and may change primary during either powered leg. The
powered solver recursively encloses possible crossings inside each RK step;
handoffs preserve system-frame position, velocity and propellant. SOI boundary
localization uses a 0.1 mm numerical tolerance. Coast surface checks cover the
actual flown interval rather than rejecting a safe arc for a later unused
periapsis. Tight-orbit departures use a six-variable damped shooting solve.
Every accepted course is rebuilt forward and must meet the same 10 m / 1 mm/s
arrival tolerances. Automatic gravity-assist optimization remains outside scope.

The departure cutoff, braking ignition, and final cutoff are separate clock
interrupts. At final cutoff the ship coasts from its integrated state and the
clock and history identify the completed approach or insertion. Cancelling
or replacing the course uses the current position, velocity, and remaining
propellant. Gravity changes also interrupt Continuous processing. See
[checkpoint version 7](SIMULATION_STATE.md).

## Performance and validation

- Solves have bounded scalar iterations and endpoint verification. A planner memo
  holds at most 256 single-leg requests for one immutable catalogue. Patched plans
  are reusable immutable results but are not automatically memoized.
- Position queries locate a segment in O(log segments), then evaluate one prepared
  conic and primary ephemeris. They never solve a trajectory or integrate frames.
- The explicit event queue handles committed transfers and burn cutoff.
  Automatic SOI detection also checks the active free coasts. Its work depends
  on how many boundaries survive radial and time-interval pruning. Near-grazing
  or persistently close geometries can still require substantially more work.
- Preliminary Windows Release sample, one shared plan and 1,000 ships: cold solve
  8.925 ms, 1,000 position queries 1.085 ms, first no-event advance 2.065 ms,
  1,000 simultaneous arrivals 1.030 ms. JIT and a single shared route limit these
  numbers; they are not sustained capacity, GPU or phone-readiness claims.

### Thirty-day automatic coast benchmark

Windows x64 Release, .NET SDK 10.0.403, Intel Core i5-11600KF. Three samples
per fleet size after a one-ship warm-up. Timings cover `MovementExecution.Advance`
only. Each ship has a distinct circular heliocentric coast whose radial range
admits Earth as a candidate. The fleet stays away from encounters for the full
30 days. The baseline is the integrated PR branch with the minute scan.

| Spacecraft | Minute scan median | Adaptive search median |
| ---: | ---: | ---: |
| 1 | 68.393 ms | 0.012 ms |
| 100 | 6,459.537 ms | 0.582 ms |
| 1,000 | 66,995.755 ms | 5.785 ms |

```powershell
. .\scripts\Use-DevEnvironment.ps1
dotnet run -c Release --file scripts/Benchmark-CoastAdvance.cs
```

This fixture measures event-free advancement with a plausible SOI candidate.
Encounter-heavy fleets and repeated short Continuous steps need separate
measurements. If those reveal repeated work, cache the next crossing and the
tick searched through for each unchanged coast and catalogue. Predicted
crossings could then use the existing event queue, invalidated on a new
coast, catalogue change, or restore. A bounded cache of parent-relative body
positions shared across ships within one advance is a separate option if
profiling shows repeated ephemeris evaluation.

Tests cover independent circular geometry, energy/angular-momentum conservation,
reverse hyperbolic propagation, both arcs, future interception, translating
primaries, deterministic queries, preview isolation, stale state/catalogue
rejection, mapless jumps, simultaneous arrivals, cancellation/replanning, SOI
continuity/events and sample/query/snapshot agreement. Commands:

```powershell
.\scripts\Verify.ps1
# Isolated disposable host only; this HTTP test changes its world:
node src/Quasar.Client/scripts/test-trajectory-api.mjs http://127.0.0.1:5093
```

The live Game Design Record was read on 8 November 2026, including Project
overview, Time behavior, and Control, orders and delegation. Edit tooling was
exposed; writes were not tested and no shared-document edits were performed.
Propulsion-aware course feasibility remains open. See [the project plan](PROJECT_PLAN.md).
