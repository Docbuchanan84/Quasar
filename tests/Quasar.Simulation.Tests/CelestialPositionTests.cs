using System.Diagnostics;
using Quasar.Server;
using Quasar.Simulation;
using Xunit.Abstractions;

namespace Quasar.Simulation.Tests;

public sealed class CelestialPositionTests(ITestOutputHelper output)
{
    private const double Mu = 398600.435;
    private static readonly CelestialBody Root = new("root", "Primary", CelestialBodyKind.Star, Mu);

    [Fact]
    public void CircularPeriodAndQuarterPeriodMatchIndependentExpectedState()
    {
        var orbit = new KeplerianOrbit(7000, 0);
        var period = KeplerianPropagator.PeriodSeconds(orbit, Mu);
        Assert.InRange(period, 5828.5166, 5828.5168);
        var quarter = KeplerianPropagator.Calculate(orbit, Mu, Ticks(period / 4));
        Near(new(0, 7000, 0), quarter.PositionKm, 1e-6);
        Near(new(-Math.Sqrt(Mu / 7000), 0, 0), quarter.VelocityKmPerSecond, 1e-9);
        Near(new(7000, 0, 0), KeplerianPropagator.Calculate(orbit, Mu, Ticks(period)).PositionKm, 1e-6);
    }

    [Fact]
    public void EccentricOrbitHasCorrectApsidesAndSpeeds()
    {
        var orbit = new KeplerianOrbit(10000, .6);
        var periapsis = KeplerianPropagator.Calculate(orbit, Mu, 0);
        var apoapsis = KeplerianPropagator.Calculate(orbit, Mu, Ticks(KeplerianPropagator.PeriodSeconds(orbit, Mu) / 2));
        Near(new(4000, 0, 0), periapsis.PositionKm);
        Near(new(-16000, 0, 0), apoapsis.PositionKm, 1e-6);
        Assert.Equal(Math.Sqrt(Mu * (2.0 / 4000 - 1.0 / 10000)), periapsis.VelocityKmPerSecond.Y, 10);
        Assert.Equal(-Math.Sqrt(Mu * (2.0 / 16000 - 1.0 / 10000)), apoapsis.VelocityKmPerSecond.Y, 10);
    }

    [Fact]
    public void InclinationNodePeriapsisAndEpochOrientPositionAndVelocity()
    {
        var epoch = TimeSpan.TicksPerDay;
        var orbit = new KeplerianOrbit(7000, 0, Math.PI / 2, Math.PI / 2, Math.PI / 2, 0, epoch);
        var state = KeplerianPropagator.Calculate(orbit, Mu, epoch);
        Near(new(0, 0, 7000), state.PositionKm);
        Near(new(0, -Math.Sqrt(Mu / 7000), 0), state.VelocityKmPerSecond);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.7)]
    [InlineData(.999999)]
    public void AnalyticVelocityMatchesDerivativeAndConservesEnergy(double eccentricity)
    {
        var orbit = new KeplerianOrbit(12000, eccentricity, .7, .3, 1.2, .1);
        const double seconds = 1234;
        var at = KeplerianPropagator.Calculate(orbit, Mu, Ticks(seconds));
        var before = KeplerianPropagator.Calculate(orbit, Mu, Ticks(seconds - .01)).PositionKm;
        var after = KeplerianPropagator.Calculate(orbit, Mu, Ticks(seconds + .01)).PositionKm;
        Near(new((after.X - before.X) / .02, (after.Y - before.Y) / .02, (after.Z - before.Z) / .02), at.VelocityKmPerSecond, 1e-7);
        var radius = at.PositionKm.DistanceTo(default);
        var v = at.VelocityKmPerSecond;
        var energy = (v.X * v.X + v.Y * v.Y + v.Z * v.Z) / 2 - Mu / radius;
        Assert.InRange(Math.Abs(energy + Mu / (2 * orbit.SemiMajorAxisKm)), 0, 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1e-12)]
    [InlineData(-1e-12)]
    [InlineData(3.141592653589793)]
    public void NearlyParabolicEllipseRemainsFiniteAtDifficultAnomalies(double mean)
    {
        var orbit = new KeplerianOrbit(7000, .999999999, MeanAnomalyAtEpochRadians: mean);
        var state = KeplerianPropagator.Calculate(orbit, Mu, 0);
        Assert.True(double.IsFinite(state.PositionKm.X));
        Assert.True(double.IsFinite(state.VelocityKmPerSecond.Y));
        Assert.InRange(state.PositionKm.DistanceTo(default), 7000 * (1 - orbit.Eccentricity) * .999999, 14000);
    }

    [Fact]
    public void HierarchyAddsParentPositionAndVelocityRegardlessOfInputOrder()
    {
        var root = Root with { ReferenceState = new(new(10, 20, 30), new(1, 2, 3)) };
        var planet = Orbiter("planet", "root", new(7000, .2, .5), 400);
        var moon = Orbiter("moon", "planet", new(100, .1, .2), 1, CelestialBodyKind.Moon);
        var asteroid = Orbiter("asteroid", "root", new(15000, .4, 1), 0, CelestialBodyKind.Asteroid);
        var service = new CelestialPositionService([moon, asteroid, planet, root]);
        var ticks = Ticks(3400);
        var p = KeplerianPropagator.Calculate(planet.Orbit!, Mu + 400, ticks);
        var m = KeplerianPropagator.Calculate(moon.Orbit!, 401, ticks);
        var actual = service.GetState("moon", ticks);
        Near(new(3410 + p.PositionKm.X + m.PositionKm.X, 6820 + p.PositionKm.Y + m.PositionKm.Y,
            10230 + p.PositionKm.Z + m.PositionKm.Z), actual.PositionKm);
        Near(new(1 + p.VelocityKmPerSecond.X + m.VelocityKmPerSecond.X, 2 + p.VelocityKmPerSecond.Y + m.VelocityKmPerSecond.Y,
            3 + p.VelocityKmPerSecond.Z + m.VelocityKmPerSecond.Z), actual.VelocityKmPerSecond);
        Assert.Equal(actual, service.GetStates(ticks)["moon"]);
        Assert.Equal(0, service.GetEnvironment("asteroid").SphereOfInfluenceRadiusKm);
        Assert.Null(service.GetEnvironment("root").SphereOfInfluenceRadiusKm);
    }

    [Fact]
    public void FutureAndPastQueriesAreDeterministicIndependentOfQueryOrder()
    {
        var body = Orbiter("planet", "root", new(7000, .3, .4, .5, .6, .7));
        var service = new CelestialPositionService([Root, body]);
        var independent = new CelestialPositionService([body, Root]);
        long[] times = [long.MaxValue, 0, -TimeSpan.TicksPerDay, TimeSpan.TicksPerDay * 36500, long.MinValue];
        foreach (var ticks in times)
        {
            var expected = independent.GetState("planet", ticks);
            Assert.Equal(expected, service.GetState("planet", ticks));
            service.GetStates(ticks == 0 ? 1 : 0);
            Assert.Equal(expected, service.GetState("planet", ticks));
        }
    }

    [Fact]
    public void HugeTimeJumpReducesPhaseAndHandlesOppositeExtremeEpochs()
    {
        // This choice produces an exactly representable one-second period.
        var orbit = new KeplerianOrbit(1, .2, EpochTicks: long.MinValue);
        var mu = Math.Tau * Math.Tau;
        Assert.Equal(1, KeplerianPropagator.PeriodSeconds(orbit, mu));
        var remainder = (long)(((decimal)long.MaxValue - long.MinValue) % TimeSpan.TicksPerSecond);
        Assert.Equal(KeplerianPropagator.Calculate(orbit with { EpochTicks = 0 }, mu, remainder),
            KeplerianPropagator.Calculate(orbit, mu, long.MaxValue));
    }

    [Fact]
    public void ReplacingElementsOrParentGravityInvalidatesDescendantsWithoutChangingOldCatalogue()
    {
        var planet = Orbiter("planet", "root", new(7000, .2), 400);
        var moon = Orbiter("moon", "planet", new(100, .1), 1, CelestialBodyKind.Moon);
        var original = new CelestialPositionService([Root, planet, moon]);
        var before = original.GetStates(TimeSpan.TicksPerDay);
        Assert.Same(before, original.GetStates(TimeSpan.TicksPerDay));
        var newOrbit = original.WithBody(planet with { Orbit = planet.Orbit! with { SemiMajorAxisKm = 8000 } });
        var newGravity = original.WithBody(planet with { GravitationalParameterKm3PerSecond2 = 800 });
        var newParent = original.WithBody(moon with { ParentId = "root" });
        foreach (var changed in new[] { newOrbit, newGravity, newParent })
            Assert.NotEqual(before["moon"], changed.GetState("moon", TimeSpan.TicksPerDay));
        Assert.NotEqual(original.GetEnvironment("moon").PeriodSeconds, newGravity.GetEnvironment("moon").PeriodSeconds);
        Assert.Equal(7000 * Math.Pow(400 / Mu, .4), original.GetEnvironment("planet").SphereOfInfluenceRadiusKm);
        original.GetStates(0);
        Assert.Equal(before["moon"], original.GetState("moon", TimeSpan.TicksPerDay));
    }

    [Fact]
    public void ConcurrentQueriesCannotMixTimestampsOrMutateSnapshots()
    {
        var service = new CelestialPositionService([Root, Orbiter("planet", "root", new(7000, .4, .6))]);
        var old = service.GetStates(0);
        var expected = old["planet"];
        Parallel.For(0, 500, i => Assert.Equal(KeplerianPropagator.Calculate(new(7000, .4, .6), Mu, i), service.GetState("planet", i)));
        Assert.Equal(expected, old["planet"]);
        Assert.False(old is IDictionary<string, CelestialState> writable && !writable.IsReadOnly);
    }

    [Fact]
    public void ThousandsOfBodiesAndDeepHierarchiesUseBoundedWorkForLargeQueries()
    {
        var definitions = Enumerable.Range(0, 10000).Select(i => Orbiter($"a{i}", "root", new(7000 + i, .2, .3), 0, CelestialBodyKind.Asteroid)).Prepend(Root);
        var service = new CelestialPositionService(definitions);
        service.GetStates(0); // Warm up; no elapsed-time stepping is involved.
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++) Assert.Equal(10001, service.GetStates(long.MaxValue - i).Count);
        watch.Stop();
        output.WriteLine($"10,001 bodies, 20 uncached extreme-time bulk queries: {watch.Elapsed.TotalMilliseconds:F3} ms (local experiment; no capacity guarantee).");
        var deep = Enumerable.Range(0, 10000).Select(i => new CelestialBody($"d{i}", "Offset", CelestialBodyKind.Other, 0,
            i == 0 ? null : $"d{i - 1}", ReferenceState: new(new(1, 0, 0), default)));
        Assert.Equal(new Position3(10000, 0, 0), new CelestialPositionService(deep).GetState("d9999", long.MaxValue).PositionKm);
    }

    [Fact]
    public void LabSnapshotAndQueriesShareAuthorityWithoutAdvancingClockHistoryOrRevision()
    {
        var lab = SystemLab.CreateLegacyFixture();
        lab.Advance(TimeSpan.FromDays(365));
        foreach (var body in lab.Snapshot().Objects.Where(o => o.Kind != "spacecraft"))
        {
            var state = lab.CelestialPositions.GetState(body.Id, lab.ElapsedTicks);
            Assert.Equal(body.Position, state.PositionKm);
            Assert.Equal(body.VelocityKmPerSecond, state.VelocityKmPerSecond);
        }
        var session = new LabSession(TimeProvider.System, SystemLab.CreateLegacyFixture());
        var before = session.Snapshot();
        session.CelestialStateAt("planet-iris", long.MaxValue);
        session.CelestialStateAt("planet-iris", long.MinValue);
        var after = session.Snapshot();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.World.ElapsedTicks, after.World.ElapsedTicks);
        Assert.Equal(before.World.Objects, after.World.Objects);
        Assert.Equal(before.World.Events, after.World.Events);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(double.PositiveInfinity, 0, 0)]
    [InlineData(7000, -1, 0)]
    [InlineData(7000, 1, 0)]
    [InlineData(7000, double.NaN, 0)]
    [InlineData(7000, 0, -1)]
    [InlineData(7000, 0, 4)]
    public void InvalidElementsAreRejected(double axis, double eccentricity, double inclination) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CelestialPositionService([Root, Orbiter("bad", "root", new(axis, eccentricity, inclination))]));

    [Fact]
    public void InvalidCataloguesAreRejectedBeforeBecomingAuthoritative()
    {
        Assert.Throws<ArgumentException>(() => new CelestialPositionService([Root, Root]));
        Assert.Throws<ArgumentException>(() => new CelestialPositionService([Root with { ParentId = "missing" }]));
        Assert.Throws<ArgumentException>(() => new CelestialPositionService([Root with { ParentId = "root" }]));
        Assert.Throws<ArgumentException>(() => new CelestialPositionService([Root with { ParentId = "child" }, Orbiter("child", "root", new(7000, 0))]));
        Assert.Throws<ArgumentException>(() => new CelestialPositionService([Root with { Orbit = new(7000, 0) }]));
        Assert.Throws<ArgumentException>(() => new CelestialPositionService([Root with { GravitationalParameterKm3PerSecond2 = 0 }, Orbiter("child", "root", new(7000, 0))]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CelestialPositionService([Root with { ReferenceState = new(new(double.NaN, 0, 0), default) }]));
        var service = new CelestialPositionService([Root]);
        Assert.Throws<KeyNotFoundException>(() => service.GetState("missing", 0));
        Assert.Throws<KeyNotFoundException>(() => service.WithBody(Root with { Id = "missing" }));
    }

    private static CelestialBody Orbiter(string id, string parent, KeplerianOrbit orbit, double mu = 0, CelestialBodyKind kind = CelestialBodyKind.Planet) =>
        new(id, id, kind, mu, parent, orbit);
    private static long Ticks(double seconds) => checked((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
    private static void Near(Position3 expected, Position3 actual, double tolerance = 1e-8) =>
        Assert.InRange(expected.DistanceTo(actual), 0, tolerance);
    private static void Near(Velocity3 expected, Velocity3 actual, double tolerance = 1e-8) =>
        Near(new Position3(expected.X, expected.Y, expected.Z), new Position3(actual.X, actual.Y, actual.Z), tolerance);
}
