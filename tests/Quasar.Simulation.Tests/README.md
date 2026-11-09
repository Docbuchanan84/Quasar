# Simulation and host-session tests

Coverage includes the Solar System and legacy fixtures, exact clock advances,
celestial queries, gravitational propagation, flybys, automatic course search,
preview and commit isolation, event ordering, and controller lease expiry.
Checkpoint and campaign cases cover save/load, compatibility, concurrent
writes, and recovery.

Run `scripts/Verify.ps1` from the repository root. This test project references the
server to exercise the session boundary in process. The simulation library is
headless. The [System Lab guide](../../docs/SYSTEM_LAB.md) and
[trajectory guide](../../docs/SPACECRAFT_TRAJECTORIES.md) describe the separate
HTTP checks for disposable running hosts.
