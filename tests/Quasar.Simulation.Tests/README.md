# Simulation and host-session tests

Agent 3 expands this to 64 total xUnit cases, including gravitational propagation,
interception, both transfer arcs, SOI patches, headless multi-ship event execution,
preview isolation, cancellation/replanning and sample/state agreement. See
`docs/SPACECRAFT_TRAJECTORIES.md`. The isolated HTTP test is
`node src/Quasar.Client/scripts/test-trajectory-api.mjs <disposable-host-url>`.
The older straight-line movement assertions were replaced with conic contracts.

Task 002 adds 21 xUnit cases covering the fixture, explicit clock boundaries,
absolute-time 3D movement, retargeting, exact arrival/interrupts, equivalent advance
increments, invalid commands, snapshot isolation, Once/Continuous controls, stale
revisions, concurrent writers, and lease ownership/expiry.

Run `scripts/Verify.ps1`. The existing test project references the server solely
to test its session boundary without an HTTP server; the simulation library has
no server dependency. `scripts/Test-SystemLabApi.ps1` separately tests a running
isolated HTTP host. See `docs/SYSTEM_LAB.md` for use and limitations.
