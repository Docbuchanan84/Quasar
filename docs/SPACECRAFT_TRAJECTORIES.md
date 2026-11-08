# Spacecraft trajectories and movement

Agent 4 adds a player preview/adjust/confirm workflow and a revision-bound
`course-preview` / `commit-course` host adapter retaining the exact solved plan.
Immediate travel buttons are replaced; existing APIs below remain compatible.
See [COURSE_PLOTTING.md](COURSE_PLOTTING.md) for the current UI and confirmation contract.

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
Delta-v sums the magnitudes of all burns. Flight time is an input; ETA is its
arrival tick. There is no claim of optimal time or fuel.

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
advances. The existing host Once/Continuous/Stop/lease controls remain authoritative.

After arrival an abstract rendezvous attachment follows the target ephemeris.
This is **not** a docking, landing, capture-orbit or stationkeeping model. It
incurs no fictional fuel expenditure. A subsequent order departs that state.

## Host and presentation

- `GET /api/lab/trajectory-preview?destinationId=planet-iris&flightSeconds=21600`
  returns a summary and samples, without changing revision, orders, clock or events.
- `GET /api/lab/spacecraft/state?atTicks=...` returns a current/future authoritative
  position and velocity under the current orders without processing events.
- `POST /api/lab/commands`: `travel` takes `destinationId` and optional
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

Inspection of committed **and working** source found no engine exhaust velocity,
wet/dry ship mass, tank or fuel accounting model. Delta-v is available. Fuel is
null with an explicit unavailability reason. No actual fuel is spent. The optional
estimator interface is an integration point, not a propulsion/inventory model.

## Performance and validation

- Solves have bounded scalar iterations and endpoint verification. A planner memo
  holds at most 256 single-leg requests for one immutable catalogue. Patched plans
  are reusable immutable results but are not automatically memoized.
- Position queries locate a segment in O(log segments), then evaluate one prepared
  conic and primary ephemeris. They never solve a trajectory or integrate frames.
- No-event advancement is O(1) apart from amortized obsolete queue cleanup;
  event handling is O(events log active movements). Advancing time does not walk
  every ship. Extracting N visible positions costs O(N); sampling is separate.
- Preliminary Windows Release sample, one shared plan and 1,000 ships: cold solve
  8.925 ms, 1,000 position queries 1.085 ms, first no-event advance 2.065 ms,
  1,000 simultaneous arrivals 1.030 ms. JIT and a single shared route limit these
  numbers; they are not sustained capacity, GPU or phone-readiness claims.

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
Propulsion and mass modelling remains open. See [the project plan](PROJECT_PLAN.md).
