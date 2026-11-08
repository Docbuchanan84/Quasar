# Simulation and host-session tests

Task 002 adds 21 xUnit cases covering the fixture, explicit clock boundaries,
absolute-time 3D movement, retargeting, exact arrival/interrupts, equivalent advance
increments, invalid commands, snapshot isolation, Once/Continuous controls, stale
revisions, concurrent writers, and lease ownership/expiry.

Run `scripts/Verify.ps1`. The existing test project references the server solely
to test its session boundary without an HTTP server; the simulation library has
no server dependency. `scripts/Test-SystemLabApi.ps1` separately tests a running
isolated HTTP host. See `docs/SYSTEM_LAB.md` for use and limitations.
