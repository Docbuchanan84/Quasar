# Solar System starter

New campaigns start paused at 2026-11-09 00:00 TDB with 38 natural bodies, two
fictional Earth installations, and Pathfinder. The object tree starts with
**Sol** open and the planets ordered by orbital size. Tap a name to select
it, or use the chevron to expand or collapse its branch. Expand **Earth**
for **Luna** and **Constructed**, which contains **O’Connor Station** and
**Virgin Galactic Star Yards**. These are tree labels for the Moon, O’Neill
Habitat, and Earth Staryard in the campaign catalogue. **Small bodies**
contains the asteroids, comets, and dwarf planets.

Select Earth and choose **Focus Selected** to see its local orbits in either
map. **Reset View** returns to the Sun-to-Neptune overview.

## Orbital data and limits

The catalogue stores geometric osculating elements from
[JPL Horizons](https://ssd.jpl.nasa.gov/horizons/manual.html), queried at
JD 2461353.5 TDB using ELEMENTS, KM-S, and the J2000 ecliptic reference plane.
The target and center IDs are recorded by each entry. Halley uses apparition
record 90000030. Planet centers are used throughout. The radii and gravitational
parameters are rounded reference values from JPL’s
[planetary data](https://ssd.jpl.nasa.gov/planets/phys_par.html),
[satellite data](https://ssd.jpl.nasa.gov/sats/phys_par/), and
[small-body data](https://ssd.jpl.nasa.gov/sbdb/). A zero GM for a small body
means its mass is omitted in this fixture.

Quasar evaluates fixed two-body ellipses in a hierarchy using elapsed simulation
time. Planetary perturbations, precession, and long-term ephemeris accuracy are
outside this approximation. The host needs no network access to run the model.
Earth Staryard uses a 42,164 km circular orbit inclined 23.4392911° to the
ecliptic. O’Neill Habitat uses a 100,000 km circular orbit inclined 35° with
a 20° ascending node. Their starting phases are 230° and 90°, respectively.

## Pathfinder propulsion

Every new campaign equips Pathfinder with the owner's exploration torch and
a full 20,000 kg tank of reaction mass; no refit is required. The preset has
300 kN thrust, 1,000 km/s effective exhaust velocity, and 10,000 kg dry mass.
It starts at 30,000 kg wet mass, about 1g acceleration, 0.3 kg/s flow, and
1,098.6 km/s theoretical delta-v. Acceleration increases as mass is spent.

This is an idealized advanced fusion torch for flight experiments. Those
numbers imply 150 GW of ideal jet power. Reactor, radiator, energy storage,
and engineering mass constraints require the later component model. The
current rocket calculation still accounts for mass loss, gravity, finite
burn time, and braking. While paused and freely coasting, **Fit exploration
torch and refuel** restores this preset and fills its tank without changing
position, velocity, or simulation time. It records a persistent refit event.
Existing checkpoints retain their saved equipment and fuel, including the
earlier chemical fixture; loading a save never applies the new-game preset.

The read-only `MovementExecution.View` and `GET /api/lab` ship status expose
these derived figures in SI units. The inspector displays delta-v in km/s.
Use **Engine burn** in the Pathfinder inspector to ignite for 1–3,600 seconds
while freely coasting and paused. Prograde and retrograde fix the direction from
the primary-relative velocity at ignition. Press **Play** to advance. The clock
interrupts at the first of the requested cutoff or propellant exhaustion. To
end a burn early, pause time and select **Cut thrust**. A saved active burn
resumes from the saved clock and tank quantity when you press **Play** again.

## Pathfinder flight

Select Earth in **Plot Course** to compare the fastest and lowest-maneuver-cost
approaches available to Pathfinder at the starting epoch. Review the propellant
cost and reserve before confirming. The [course planner](COURSE_PLOTTING.md)
reports when Pathfinder's installed engine and fuel cannot execute a candidate.
Older impulsive courses may include a simplified single-planet flyby.

The torch also supplies executable Mercury, Venus, and Mars approaches from
the two-hour starter departure tested for this change. Routes still depend on
geometry and the bounded solver. A single-primary approach stops outside the
destination's modeled SOI. Moon transfers and planetary capture can require
gravity transitions that this planner does not support.

Checkpoint format 2 stores body radii and the optional flyby encounter.
Version 1 checkpoints retain their original catalogue and courses when loaded.
