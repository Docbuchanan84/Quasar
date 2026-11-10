# Authoritative simulation checkpoints

[Issue #10](https://github.com/Docbuchanan84/Quasar/issues/10) adds a headless
checkpoint boundary in `Quasar.Simulation`. The format describes the current
physics model and the current single-ship System Lab.

## Capture, encode, and restore

```csharp
SystemLabState state = lab.ExportState(); // Under the simulation writer or session gate.
byte[] payload = SimulationStateJson.Serialize(state);

SystemLabState decoded = SimulationStateJson.Deserialize(payload);
SystemLab candidate = SystemLab.RestoreState(decoded);
```

- `SystemLabState` contains `Simulation`, `LastEventSequence`, and ordered
  `History` entries. `SimulationState` contains `FormatVersion`,
  `ElapsedTicks`, `Bodies`, and `Spacecraft`. The nested
  `Simulation.FormatVersion` versions the entire checkpoint. The current version
  is `SimulationState.CurrentFormatVersion` (6). Version 6 adds finite courses.
  Version 5 records automatic
  primary selection on eligible free coasts. Older free coasts keep their
  stored primary until a new coast is created by cancellation or burn cutoff.
  Version 4 added an optional active finite burn. Version 3 stores optional propulsion and current propellant.
  Versions 1 and 2 restore without propulsion and retain their original catalogue.
  Version 2 introduced body radii and optional flyby metadata.
- `CelestialBodyState` and `OrbitState` carry stable IDs, hierarchy, GM,
  reference state and epoch, and orbital elements and epoch. `SpacecraftState`
  carries the ship ID, gravity primary (last primary while in rendezvous), and
  exactly one of `CoastState`, `CommittedTrajectoryState`, `RendezvousState`,
  `ActiveBurnState`, or `FiniteCourseState`.
- A coast stores its epoch and initial primary-relative position and velocity.
  A committed plan stores its departure state, target, ordered conic segments,
  impulses, fuel estimate, and zero-based `NextBoundaryIndex`. The segment
  end ticks at and after that index are the pending patch and arrival
  boundaries. An arrived ship stores the target ephemeris or inertial waypoint
  and arrival tick, and follows the target's motion.
- An active burn saves the ignition epoch, primary-relative initial state,
  inertial unit direction, requested duration, and ignition propellant. The
  ship also stores the quantity remaining at the checkpoint clock. Restore
  reconstructs the powered segment from ignition, verifies the current quantity
  and active interval, and schedules the cutoff or exhaustion event once.
- A finite course stores both burn vectors and durations, its exact departure
  state and ignition equipment/fuel, the gravity primary, and requested
  arrival state. Restore rebuilds the burns and analytical coast, checks the
  endpoint and current propellant, and schedules the next boundary from the
  clock. Completed finite courses store a free coast at the achieved state.
- Export is a detached immutable capture. Bodies and ships are exported in
  ordinal ID order. History, segments, and burns retain their execution order.
  Use the same single-writer synchronization as for world commands. Encoding can
  run after releasing that synchronization.
- Use `SimulationStateJson` for UTF-8 JSON. Fields use camel case, every signed
  64-bit field uses an invariant decimal string (including ticks and event
  sequence), and celestial kinds use camel-case enum names. Nullable fields
  are generally present with JSON null when unused. Absent propulsion, active
  burns, and finite courses are omitted. The decoder rejects missing,
  unknown, or duplicate fields, invalid tick strings, and unsupported versions.
  It checks the format and shape. The restore factory checks the relationships
  and physics. Malformed payloads and invalid state throw `InvalidDataException`.
- `MovementExecution.ExportState()` and
  `MovementExecution.RestoreState(SimulationState)` also support standalone
  headless multi-ship worlds. The lab factory additionally checks its Pathfinder
  identity and a supported stellar primary (the Sun or legacy Helios) and
  verifies history against the saved motion.
- Restore constructs a separate catalogue and execution owner, checks the solved
  conics and burn continuity, and enqueues only the next live boundary per
  ship. Later boundaries are scheduled as they are reached. It reconstructs
  conic coefficients from their saved initial conditions and GM. A Lambert solve
  is unnecessary. Planner/ephemeris caches and display samples are derived.
  Preview tokens, run controls, and browser state are outside the payload.

## Storage handoff

The [host-side SQLite store](SQLITE_PERSISTENCE.md) persists the exact UTF-8
bytes produced by `SimulationStateJson` alongside a signed 64-bit clock, a
simulation format version, and a SHA-256 digest. It checks the size, digest,
metadata, decoded format and clock, and detached `SystemLab.RestoreState`
before returning a checkpoint. Storage schema and simulation format have
separate versions. The [host lifecycle](CAMPAIGN_LIFECYCLE.md) installs the
validated candidate and starts it stopped.

## Verification

`scripts/Verify.ps1` covers the checkpoint regression cases and existing suites.
Checkpoint cases cover midflight JSON round trips, exact 15-day arrival
interruptions, checkpoints before and at a patch, stable simultaneous events,
cancelled free coasts, moving rendezvous, flyby and Moon course reload,
history sequence and ordering, signed 64-bit tick limits, and invalid
state/JSON rejection without active-world mutation.
