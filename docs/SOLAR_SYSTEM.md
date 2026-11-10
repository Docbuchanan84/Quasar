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

New campaigns equip Pathfinder with a provisional chemical rocket fixture.
Its base dry mass is 8,000 kg, its engine and empty tank weigh 1,000 kg each,
and the tank starts with 10,000 kg of its 10,000 kg capacity. The engine is
3 m long and 1.5 m in diameter, with 200 kN rated thrust and 3,000 m/s
effective exhaust velocity. Both components use the `test-chemical` propellant ID.

The resulting dry and wet masses are 10,000 and 20,000 kg. Rated mass flow
is about 66.67 kg/s, initial thrust acceleration is 10 m/s², and theoretical
impulsive delta-v is about 2.079 km/s. An empty tank retains rated flow as
an engine specification and has zero available thrust acceleration and delta-v.

The read-only `MovementExecution.View` and `GET /api/lab` ship status expose
these derived figures in SI units. The inspector displays delta-v in km/s.
Use **Engine burn** in the Pathfinder inspector to ignite for 1–3,600 seconds
while freely coasting and paused. Prograde and retrograde fix the direction from
the primary-relative velocity at ignition. Press **Play** to advance. The clock
interrupts at the first of the requested cutoff or propellant exhaustion. To
end a burn early, pause time and select **Cut thrust**. A saved active burn
resumes from the saved clock and tank quantity when you press **Play** again.

## Pathfinder flight

Select Mercury in **Plot Course** to compare the fastest and lowest
maneuver-cost routes found within the default 25 km/s test budget. At the
starting epoch, the search can find routes via Venus. The ship's speed changes
along each solar coast. At the flyby the two arcs meet at Venus's center in the
simplified model. The predicted closest approach describes the implied
hyperbola. The map depicts the two coasts at system scale.

The [course planner](COURSE_PLOTTING.md) checks direct and one-planet routes
across candidate arrival times, including short and long zero-revolution arcs.
A flyby needs matching planet-relative excess speeds within 0.001 km/s and a
predicted closest approach at least 1,000 km above the reference radius. The
planner uses instantaneous test maneuvers. Finite engine burns are separate
orders and spend propellant only as simulation time advances.

Checkpoint format 2 stores body radii and the optional flyby encounter.
Version 1 checkpoints retain their original catalogue and courses when loaded.
