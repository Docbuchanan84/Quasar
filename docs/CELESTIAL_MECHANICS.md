# Celestial mechanics and position queries

**Agent 3 update:** [SPACECRAFT_TRAJECTORIES.md](SPACECRAFT_TRAJECTORIES.md) now
implements the separate spacecraft planning/execution task. Its lab transfers
replace the historical constant-speed travel preservation described below.
The celestial service itself remains unchanged.

Implemented for the owner's **Agent 2: Celestial Mechanics and Position Service**
request on 8 November 2026. This headless simulation feature is independent of
ASP.NET, renderer frames, cameras, and connected clients. No runtime dependencies
were added. The existing System Lab's fixed planets and travel orders are
preserved; switching to moving targets requires a separate navigation task.

## Contracts and ownership

| Interface/type | Responsibility |
| --- | --- |
| `CelestialBody` | Immutable simulation data: identity, kind, own GM, optional parent/ellipse, or parent-relative reference position/velocity/epoch |
| `KeplerianOrbit` | Semimajor axis, eccentricity, inclination, ascending node, argument of periapsis, mean anomaly and epoch |
| `KeplerianPropagator` | Pure analytical relative position/velocity; no world or clock access |
| `ICelestialPositionService` | `GetState(bodyId, atTicks)`, `GetStates(atTicks)`, `GetEnvironment(bodyId)` |
| `CelestialPositionService` | Validates/owns one immutable catalogue, composes parent states and caches results |
| `OrbitalEnvironment` | Own GM, relative GM, derived period and approximate sphere of influence |

The simulation owns definitions and authoritative `SystemLab.ElapsedTicks`.
Snapshots query at that clock. Queries at other timestamps do **not** change the
clock, events, orders, revision, or physical data. The memo is derived data only.
Callers can retain immutable bulk results across subsequent queries.

Coordinates use a right-handed inertial system-local XYZ frame: position in km,
velocity in km/s, GM in km³/s², angles in radians. The reference plane is XY;
inclination is measured from +Z, ascending node from +X toward +Y, and argument
of periapsis within the orbital plane from that node. Child orbital axes use the
same frame, not a rotating parent-body frame. Parent positions **and velocities**
are added to child relative states. Stars, planets, moons, asteroids and other
bodies use the same calculation; kinds do not select different physics.

Query timestamps/epochs are signed Int64 .NET ticks (100 ns) relative to the same
simulation origin. Negative queries are allowed although the lab clock remains
nonnegative. No Earth calendar/J2000 conversion is implied. This spans roughly
29,000 years on each side of the origin. Decimal subtraction avoids overflow even
between opposite Int64 endpoints; phase is reduced before returning to doubles.

## Headless usage

```csharp
var positions = new CelestialPositionService([
    new("star", "Primary", CelestialBodyKind.Star, 1.32712440018e11),
    new("planet", "Planet", CelestialBodyKind.Planet, 398600.435,
        ParentId: "star", Orbit: new(149597870.7, .0167, .05)),
    new("moon", "Moon", CelestialBodyKind.Moon, 4902.8,
        ParentId: "planet", Orbit: new(384400, .0549, .09))
]);
var futureTicks = TimeSpan.FromDays(36500).Ticks;
CelestialState future = positions.GetState("moon", futureTicks);
IReadOnlyDictionary<string, CelestialState> all = positions.GetStates(futureTicks);
OrbitalEnvironment environment = positions.GetEnvironment("moon");

// A deliberate simulation definition change creates a validated replacement.
var body = positions.Bodies["planet"];
var revised = positions.WithBody(body with {
    Orbit = body.Orbit! with { Eccentricity = .02 }
});
// Install 'revised' as the simulation's catalogue when accepting that change.
// Retained old catalogues/results remain internally consistent.
```

Without an orbit, `ReferenceState` propagates linearly from `ReferenceEpochTicks`
relative to its parent. Zero velocity represents a fixed offset. An orbit replaces
that reference state. Roots may have constant inertial velocity; an orbit always
requires a parent. Duplicate identities, missing parents, cycles, nonfinite inputs
and invalid ellipses are rejected. Propagation beyond finite state range fails
instead of returning NaN/infinity. Lab GM values are illustrative fixture inputs,
not measured astronomical data about these fictional planets.

`WithBody` requires an existing ID and rebuilds/validates the catalogue. This
invalidates **all** derived states/environments, including descendants and
parent-GM-dependent periods/SOI. Add/remove uses a newly constructed catalogue.
Definition edits are expected to be rare: rebuilding is O(N). This does not
select a permanent world-edit/save architecture. No definition-edit HTTP endpoint.

## Propagation and performance

Relative GM is `parent.GM + body.GM`; period derives from
`2π sqrt(a³ / GM)` rather than independently stored inputs that could disagree.
Mean anomaly advances analytically; a bracketed Newton solve finds eccentric
anomaly. Orbital-plane position and its analytical derivative rotate by
node/inclination/periapsis into XYZ. No per-second or per-tick integration.

Compiled rotation bases/periods are retained. Hierarchy validation and bulk
evaluation use iterative topological traversal; one-object queries evaluate only
uncached ancestors. One timestamp memo holds O(N) states, reusing its immutable
bulk result. Another timestamp discards the memo. Internal locking prevents
concurrent query timestamp mixing. Alternating forecast/live queries may evict
one another without affecting correctness. Caller-retained results/catalogues
consume additional memory outside this memo.

A warmed Release experiment on this PC evaluated **10,001 bodies at 20 uncached
near-Int64.MaxValue timestamps in 133.035 ms total**. The same test queries a
10,000-level hierarchy without recursion. This measures synthetic headless
calculations, not HTTP throughput, rendering, galaxy capacity, or iPhone
performance. CI does not assert a wall-clock threshold.

## Host and presentation integration

`GET /api/lab/celestial/{bodyId}/state?atTicks=<signed Int64>` returns:

```json
{
  "bodyId": "planet-iris",
  "atTicks": "9223372036854775807",
  "positionKm": { "x": 1200000, "y": 400000, "z": 200000 },
  "velocityKmPerSecond": { "x": 0, "y": 0, "z": 0 }
}
```

The timestamp string preserves all 64 bits in JavaScript. Missing/malformed or
out-of-range timestamps return 400; unknown bodies return 404. Responses retain
no-store. Queries use the current fixed-body lab catalogue without UI updates or
time advancement. Spacecraft IDs are outside this service. `/api/lab` snapshots
also get celestial positions/velocities from the service; spacecraft velocity is
null because spacecraft propagation is outside this interface/task.

Both maps consume the same snapshot positions. `mapCoordinates.ts` scales km by
1/1,000,000 for Babylon's right-handed XYZ and Pixi's XY with inverted screen Y.
No map extrapolates or writes physical state. Shared reference circles remain
**illustrative lab geometry**, not physical elliptical orbits. Orbit-path
presentation remains a future task.

## Verification and limits

`scripts/Verify.ps1` runs 48 simulation/session cases (27 new celestial cases),
format, lint, renderer loading/bundling, two coordinate/guide tests, and the
TypeScript/Vite build. Tests cover independent circular/apsis expectations,
period, orientation/epoch, analytical velocity/energy, difficult eccentricities,
hierarchies, past/future/extreme queries, deterministic query order, concurrent
reads, definition/GM/reparent replacement, invalid inputs and large catalogues.
Existing travel/arrival/session behavior remains covered.

With an isolated Release host on loopback 5091:

```powershell
node src/Quasar.Client/scripts/test-celestial-api.mjs http://127.0.0.1:5091
.\scripts\Test-SystemLabApi.ps1 -BaseUrl http://127.0.0.1:5091
```

The first checks exact signed timestamps, HTTP validation, projection agreement,
and unchanged clock/revision/history. The second mutates only that isolated lab
to verify existing commands/runner/boundary behavior. No new physical-phone or
browser GPU validation was performed for this task.

- Bound ellipses only (`0 <= e < 1`), finite positive semimajor axis, inclination
  in `[0, π]`, derived periods 100 ns through 1e20 seconds. Parabolic/hyperbolic
  trajectories are rejected.
- Independent hierarchical two-body approximation: no N-body perturbations,
  reflex/barycentric motion, precession, relativity, tides, collisions, maneuvers,
  or automatic parent/SOI transitions. An orbiting star is representable, but
  this does not implement a self-consistent binary-star barycentre.
- Laplace SOI is `a * (body.GM / parent.GM)^(2/5)`, an approximate planning hint.
  Massless orbiters have zero SOI; roots/nonorbiting definitions return null.
  Comparable-mass systems are outside the useful approximation. This is neither
  a Hill radius nor an enforced gravity cutoff.
- Double precision and rounded derived periods limit long-range phase accuracy.
  Decimal reduction preserves tick arithmetic, not unavailable element accuracy.
  Same-build determinism is tested; bitwise cross-platform/runtime replay is not
  promised.
- Queries apply the selected catalogue's elements to **all** timestamps.
  Historical element changes, ephemeris segments, and forecast validity/history
  are not modeled. Preserve a prior catalogue when that distinction matters.
- No moving-target orders, course plotting, persistent saves, background
  precomputation, deployment changes, or permanent scheduler were added.

Element/state conventions were checked against the primary
[NASA/JPL NAIF conic-state interface](https://naif.jpl.nasa.gov/pub/naif/toolkit_docs/C/cspice/conics_c.html)
on 8 November 2026. This implementation uses semimajor axis rather than NAIF's
periapsis distance and simulation ticks rather than J2000; it does not import
SPICE or claim its full conic support/accuracy.
The semimajor-axis SOI approximation follows the mean-distance convention in
[NASA's planet sphere-of-influence table](https://ntrs.nasa.gov/api/citations/19690010669/downloads/19690010669.pdf).

Source context: live **Quasar — Game Design Record** read on 8 November 2026,
revision prefix `AHj4eMQHPFgN`, including the Task 002 update. Earlier pre-production
status is historical; later implementation entries/code describe current status.
Read access was verified; write tools were present but write access was not
tested and the shared record was not modified.

Next recommended task: recoverable versioned save/load including celestial
definitions/epochs. Its format still requires a concrete task.
