# Sundiver sail trial

In a paused Solar System campaign, select **Launch Sundiver sail trial**. It adds
Sundiver alongside Pathfinder and places it on an inbound Earth flyby about
four simulated days from closest approach. The trial is available in existing
Solar System campaigns and in new ones. Launch is an explicit campaign order.

1. Select **Preview coast** to see the ballistic encounter.
2. Set the angle to **+35°** and the duration to **1 day**. Select **Compare trim
   with coast**. Both maps show the coast in blue and the trim followed by
   coast in pink. The inspector compares predicted closest altitude and
   heliocentric speed change across the encounter.
3. Select **Confirm sail trim**, set a **15 days** advance interval in **Once**
   mode, and select **Play**. The sail feathers automatically at the cutoff.
   Sundiver coasts into and out of Earth’s gravity sphere. Use **Feather sail
   now** while paused to end a trim early. You can save in either flight mode.

Select Sundiver while flying to see its cyan **Flight path**. It includes the
rest of the current trim, the coast after cutoff, and predicted gravity
handoffs within the forecast window.

At the starter epoch, the feathered coast passes 14,000 km above Earth and
gains 4.12 km/s of Sun-relative speed across the encounter. The one-day +35°
trim changes that to about 3,503 km and 5.69 km/s. Launch date and sail order
affect the result.

Positive angle raises heliocentric orbital energy by pushing along the
transverse velocity. Negative angle lowers it by pushing against that
velocity. The reflected force always has an anti-sunward component. At the
trial’s Earth distance, a 140 kg ship with 12,000 m² of reflective sail gets
about 0.8 mm/s² face-on. Tilting reduces thrust, and doubling solar distance
quarters it. This is an ideal flat-mirror model based on solar radiation
pressure. [NASA NEA Scout thrust and torque model](https://ntrs.nasa.gov/api/citations/20170001502/downloads/20170001502.pdf?attachment=true)

## Simulation and limits

- `MovementExecution` holds one propulsion component on each spacecraft.
  Pathfinder uses its existing rocket and tank record. Sundiver uses a
  `SolarSail` record with area and mass. Both use the existing spacecraft
  registry, time advancement, analytical coasts, and gravity transitions.
- Powered trajectories share the RK4 integration step and use different
  force laws. The sail computes thrust from the current Sun-relative state.
  It integrates a bounded trim at fixed epochs, and samples intermediate
  states without depending on the player's clock interval.
- Trim commands require a current, reviewed preview. A trim stays inside a
  single gravity region. It ends before a handoff, or the player feathers and
  coasts through. The flyby has no instantaneous velocity change. The speed
  difference reported in the inspector compares the beginning and end of the
  ballistic planet-relative segment in the Sun's frame.
- Checkpoint v8 adds optional sail equipment and active trim state. Restore
  rebuilds the physical path and boundary using the saved initial conditions.
  Existing checkpoints through v7 retain their original ships and trajectories.
- The trial uses a prepared encounter and manual sail angles. It models
  ideal reflective thrust and patched-conic gravity. Eclipse, attitude slew,
  thermal limits, and automatic sail route search are open.
