# Solar System starter

New campaigns start paused at 2026-11-09 00:00 TDB with 38 natural bodies, two
fictional Earth installations, and Pathfinder. Choose Earth from the grouped
object list, then **Focus Selected** to see the Moon, O’Neill Habitat, and Earth
Staryard in either map. Reset View returns to the Sun-to-Neptune overview. The
list also exposes objects beyond the default view.

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

## Pathfinder

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
ship uses instantaneous test maneuvers. Finite thrust and fuel expenditure are
reserved for propulsion work.

Checkpoint format 2 stores body radii and the optional flyby encounter.
Version 1 checkpoints retain their original catalogue and courses when loaded.
