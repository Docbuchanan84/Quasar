# Fusion Torch Designer

Open **Torch Designer** from Pathfinder’s Propulsion panel or go directly to
`/torch-designer/`. Start from stock, change one setting, and look for the
performance differences below the diagram. The three controls set jet power,
effective exhaust speed, and magnetic nozzle diameter. Exact numeric inputs
and sliders edit the same draft. Reset restores the stock settings.

**Run test burn** runs stock and draft for 30 simulated seconds from identical
full-tank Pathfinder fixtures. Velocity change is measured against an
unpowered coast from the same starting state. **Fit to Pathfinder** is a
separate, confirmed action on the current campaign. Pause time and finish or
cancel any course before fitting. If a manual burn is active, cut thrust first.
The fit requires the torch-compatible reaction-mass tank and preserves its
remaining fuel. Save the campaign normally to persist the fitted drive.

## Model and interfaces

Stock is 150 GW, 1,000 km/s and 1.5 m. With SI inputs, thrust is twice the jet
power divided by effective exhaust speed, and flow is thrust divided by
effective exhaust speed. Jet power is the kinetic power delivered to the
exhaust. The drive assumes its power is supplied by the ship. The reactor
is a separate future component.

The fictional nozzle limit is the lower of 300 GW and 150 GW times the
squared ratio of diameter to 1.5 m. Drive dry mass is 400 kg plus 600 kg times
that same squared ratio. Length is fixed at 3 m. The diagram is schematic.

The same C# evaluation validates preview, test burn and installation.
`GET /api/torch-designer/preview` and `/test-burn` accept `jetPowerGw`,
`exhaustSpeedKmPerSecond`, and `nozzleDiameterM`. The read-only
`/fit-status` supplies the campaign version and any blocking reason.
`POST /api/lab/commands` accepts `fit-designed-torch` with a nested
`torchDesign` and the usual instance, revision and client identifiers.
The browser stores the draft locally. The checkpoint stores the realized
engine using the existing propulsion fields and records a refit event.
