namespace Quasar.Simulation;

/// <summary>Pure two-body ellipse propagation. No clock, world mutation, cache, or renderer.</summary>
public static class KeplerianPropagator
{
    public static double PeriodSeconds(KeplerianOrbit orbit, double relativeMuKm3PerSecond2)
    {
        Validate(orbit, relativeMuKm3PerSecond2);
        // This ordering avoids cubing a possibly large semimajor axis.
        var period = Math.Tau * orbit.SemiMajorAxisKm / Math.Sqrt(relativeMuKm3PerSecond2 / orbit.SemiMajorAxisKm);
        // Decimal phase reduction supports the full signed tick domain without subtraction overflow.
        if (!double.IsFinite(period) || period < 1e-7 || period > 1e20)
            throw new ArgumentOutOfRangeException(nameof(orbit), "Supported periods are 100 ns through 1e20 seconds.");
        return period;
    }

    public static CelestialState Calculate(KeplerianOrbit orbit, double relativeMuKm3PerSecond2, long atTicks) =>
        Compile(orbit, relativeMuKm3PerSecond2).Calculate(atTicks);

    internal static CompiledOrbit Compile(KeplerianOrbit orbit, double mu) => new(orbit, PeriodSeconds(orbit, mu));

    private static void Validate(KeplerianOrbit orbit, double mu)
    {
        ArgumentNullException.ThrowIfNull(orbit);
        if (!double.IsFinite(mu) || mu <= 0) throw new ArgumentOutOfRangeException(nameof(mu));
        if (!double.IsFinite(orbit.SemiMajorAxisKm) || orbit.SemiMajorAxisKm <= 0
            || !double.IsFinite(orbit.Eccentricity) || orbit.Eccentricity < 0 || orbit.Eccentricity >= 1
            || !double.IsFinite(orbit.InclinationRadians) || orbit.InclinationRadians < 0 || orbit.InclinationRadians > Math.PI
            || !double.IsFinite(orbit.LongitudeOfAscendingNodeRadians)
            || !double.IsFinite(orbit.ArgumentOfPeriapsisRadians) || !double.IsFinite(orbit.MeanAnomalyAtEpochRadians))
            throw new ArgumentOutOfRangeException(nameof(orbit), "Finite elliptic elements and inclination in [0, pi] are required.");
    }

    internal sealed class CompiledOrbit
    {
        private readonly KeplerianOrbit orbit;
        private readonly Position3 p;
        private readonly Position3 q;
        private readonly double minorAxisRatio;
        public double PeriodSeconds { get; }

        public CompiledOrbit(KeplerianOrbit orbit, double periodSeconds)
        {
            this.orbit = orbit;
            PeriodSeconds = periodSeconds;
            var (sinNode, cosNode) = Math.SinCos(Math.IEEERemainder(orbit.LongitudeOfAscendingNodeRadians, Math.Tau));
            var (sinPeriapsis, cosPeriapsis) = Math.SinCos(Math.IEEERemainder(orbit.ArgumentOfPeriapsisRadians, Math.Tau));
            var (sinInclination, cosInclination) = Math.SinCos(orbit.InclinationRadians);
            p = new(cosNode * cosPeriapsis - sinNode * sinPeriapsis * cosInclination,
                sinNode * cosPeriapsis + cosNode * sinPeriapsis * cosInclination, sinPeriapsis * sinInclination);
            q = new(-cosNode * sinPeriapsis - sinNode * cosPeriapsis * cosInclination,
                -sinNode * sinPeriapsis + cosNode * cosPeriapsis * cosInclination, cosPeriapsis * sinInclination);
            minorAxisRatio = Math.Sqrt((1 - orbit.Eccentricity) * (1 + orbit.Eccentricity));
        }

        public CelestialState Calculate(long atTicks)
        {
            var seconds = ((decimal)atTicks - orbit.EpochTicks) / TimeSpan.TicksPerSecond;
            var phase = (double)(seconds % (decimal)PeriodSeconds) / PeriodSeconds;
            var mean = Math.IEEERemainder(Math.IEEERemainder(orbit.MeanAnomalyAtEpochRadians, Math.Tau) + Math.Tau * phase, Math.Tau);
            var eccentric = SolveEccentricAnomaly(mean, orbit.Eccentricity);
            var (sinE, cosE) = Math.SinCos(eccentric);
            var x = orbit.SemiMajorAxisKm * (cosE - orbit.Eccentricity);
            var y = orbit.SemiMajorAxisKm * minorAxisRatio * sinE;
            var rate = Math.Tau / PeriodSeconds / (1 - orbit.Eccentricity * cosE);
            var vx = -orbit.SemiMajorAxisKm * sinE * rate;
            var vy = orbit.SemiMajorAxisKm * minorAxisRatio * cosE * rate;
            var result = new CelestialState(new(p.X * x + q.X * y, p.Y * x + q.Y * y, p.Z * x + q.Z * y),
                new(p.X * vx + q.X * vy, p.Y * vx + q.Y * vy, p.Z * vx + q.Z * vy));
            result.RequireFinite();
            return result;
        }
    }

    private static double SolveEccentricAnomaly(double mean, double eccentricity)
    {
        if (mean == 0 || eccentricity == 0) return mean;
        var lower = -Math.PI;
        var upper = Math.PI;
        var value = eccentricity < .8 ? mean : Math.CopySign(Math.PI, mean);
        // Newton with a monotonic bracket: bounded work, including near-parabolic ellipses.
        for (var iteration = 0; iteration < 96; iteration++)
        {
            var residual = value - eccentricity * Math.Sin(value) - mean;
            if (residual == 0) return value;
            if (residual > 0) upper = value; else lower = value;
            var next = value - residual / (1 - eccentricity * Math.Cos(value));
            if (!double.IsFinite(next) || next <= lower || next >= upper) next = (lower + upper) / 2;
            if (Math.Abs(next - value) <= 2e-15) return next;
            value = next;
        }
        throw new ArithmeticException("Kepler equation did not converge.");
    }
}
