# Simulation and host-session tests

The integrated course release has 99 xUnit cases. Coverage includes the fixture
and exact clock advances, celestial queries and projection data, gravitational
propagation and interception, event ordering, course preview/commit isolation,
cancellation, stale revisions, concurrent writers, and controller lease expiry.

Run `scripts/Verify.ps1` from the repository root. This test project references the
server to exercise the session boundary in process. The simulation library is
headless. The [System Lab guide](../../docs/SYSTEM_LAB.md) and
[trajectory guide](../../docs/SPACECRAFT_TRAJECTORIES.md) describe the separate
HTTP checks for disposable running hosts.
