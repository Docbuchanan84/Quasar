# Surveyor fission-electric ship

In a paused Solar System campaign, choose **Launch Surveyor**. It starts
outside Earth's gravity sphere in solar orbit. Select **Plot Course**, choose
**Mars**, and leave arrival set to **Match speed near destination** to try the
starter mission. Compare **Fastest**, **Most efficient**, and **Most direct**.
Open **Maneuvers and navigation** to inspect the departure and braking burns.
After confirming a route, use 30-day Continuous advance to fly it. The clock
pauses at the verified approach.

At the starter epoch, the fastest route found took 285.7 simulated days and
spent 2,657 kg of argon. It used a 2.16-day departure burn and a 1.94-day
braking burn. The most efficient option found took 297.1 days and spent
2,604 kg. These results depend on departure geometry and the search.

## Installed propulsion

The 20,000 kg fueled ship carries 6,000 kg of argon. The prototype assumes an
installed fission-electric power supply delivering 10 MW to the thruster.
At 60% jet efficiency and 40 km/s exhaust speed, the drive produces 300 N
at full power, consuming 7.5 g/s. Initial full-power acceleration is
0.015 m/s². These are prototype equipment values. Reactor operation, radiator
constraints, and changes in operating efficiency are future engineering work.
[JPL electric propulsion performance model](https://descanso.jpl.nasa.gov/SciTechBook/series4/Electric_Propulsion_2nd_edition.pdf)

The electric drive supplies thrust, exhaust speed, equipment mass, and burn
limits through the same propellant-drive interface as the torch. The common
solver uses those values for finite burns, changing mass, clearance, gravity
handoffs, braking, and arrival validation. Long burns use bounded integration
steps that get smaller near gravity primaries. Pathfinder retains its
one-second steps. The UI uses the same destination orders for all three ships.

Surveyor is an opt-in ship in both new and existing Solar System campaigns.
Checkpoint v10 stores its loadout, remaining argon, and active course.
The [shared component direction](DECISIONS.md#approved--shared-component-models-2026-11-14)
also applies to future equipment families such as sensors.
