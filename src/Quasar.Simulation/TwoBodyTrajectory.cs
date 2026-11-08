namespace Quasar.Simulation;

internal readonly record struct Vector3d(double X, double Y, double Z)
{
    public static Vector3d From(Position3 p) => new(p.X, p.Y, p.Z);
    public static Vector3d From(Velocity3 v) => new(v.X, v.Y, v.Z);
    public Position3 Position => new(X, Y, Z);
    public Velocity3 Velocity => new(X, Y, Z);
    public double Length => Math.Sqrt(Dot(this, this));
    public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vector3d Cross(Vector3d a, Vector3d b) => new(a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3d operator *(Vector3d a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static Vector3d operator /(Vector3d a, double b) => a * (1 / b);
}

/// <summary>One unforced conic in a primary-relative inertial frame; queries never integrate time steps.</summary>
public sealed class TwoBodyTrajectory
{
    private readonly Vector3d position, velocity;
    private readonly double mu, radius, alpha, radialProduct, sqrtMu;
    public long EpochTicks { get; }
    public double PeriapsisKm { get; }
    public double? PeriodSeconds { get; }
    public CelestialState InitialState { get; }

    public TwoBodyTrajectory(CelestialState relativeState, long epochTicks, double gravitationalParameter)
    {
        relativeState.RequireFinite();
        if (!double.IsFinite(gravitationalParameter) || gravitationalParameter <= 0)
            throw new ArgumentException("A gravity-aware coast requires a finite positive primary GM.");
        InitialState = relativeState;
        EpochTicks = epochTicks;
        mu = gravitationalParameter;
        sqrtMu = Math.Sqrt(mu);
        position = Vector3d.From(relativeState.PositionKm);
        velocity = Vector3d.From(relativeState.VelocityKmPerSecond);
        radius = position.Length;
        if (radius <= 0 || !double.IsFinite(radius)) throw new ArgumentException("Position must be away from the primary centre.");
        radialProduct = Vector3d.Dot(position, velocity);
        alpha = 2 / radius - Vector3d.Dot(velocity, velocity) / mu;
        var angularMomentum = Vector3d.Cross(position, velocity);
        var p = Vector3d.Dot(angularMomentum, angularMomentum) / mu;
        var eccentricity = Math.Sqrt(Math.Max(0, 1 - alpha * p));
        PeriapsisKm = p / (1 + eccentricity);
        if (!double.IsFinite(PeriapsisKm) || PeriapsisKm < 1e-6)
            throw new ArgumentException("Radial/collision conics are unsupported; choose a nonzero clearance and transverse velocity.");
        if (alpha > 0) PeriodSeconds = 2 * Math.PI / Math.Sqrt(mu * alpha * alpha * alpha);
    }

    public CelestialState StateAt(long atTicks)
    {
        var seconds = ((decimal)atTicks - EpochTicks) / TimeSpan.TicksPerSecond;
        // Reduce full revolutions before conversion to double, including very large clock jumps.
        if (PeriodSeconds is { } period && period > 0 && period < (double)decimal.MaxValue)
            seconds %= (decimal)period;
        var dt = (double)seconds;
        if (dt == 0) return InitialState;
        var sign = Math.Sign(dt);
        double Equation(double positiveChi)
        {
            var chi = sign * positiveChi;
            var (c, s) = Stumpff(alpha * chi * chi);
            return sign * (radialProduct / sqrtMu * chi * chi * c
                + (1 - alpha * radius) * chi * chi * chi * s + radius * chi - sqrtMu * dt);
        }
        var low = 0d;
        var high = Math.Max(1, Math.Sqrt(radius));
        var bracketed = false;
        for (var i = 0; i < 128; i++)
        {
            var value = Equation(high);
            if (value >= 0 || double.IsPositiveInfinity(value)) { bracketed = true; break; }
            if (double.IsNaN(value)) break;
            high *= 2;
        }
        if (!bracketed) throw new InvalidOperationException("Coast propagation exceeded the supported numerical range.");
        var candidate = (low + high) / 2;
        var solved = false;
        for (var i = 0; i < 100; i++)
        {
            var value = Equation(candidate);
            if (Math.Abs(value) <= Math.Max(1e-9, sqrtMu * Math.Abs(dt) * 1e-14)) { solved = true; break; }
            if (value < 0) low = candidate; else high = candidate;
            var chi = sign * candidate;
            var z = alpha * chi * chi;
            var (c, s) = Stumpff(z);
            var derivative = radialProduct / sqrtMu * chi * (1 - z * s)
                + (1 - alpha * radius) * chi * chi * c + radius;
            var next = candidate - value / derivative;
            candidate = double.IsFinite(next) && next > low && next < high ? next : (low + high) / 2;
        }
        if (!solved) throw new InvalidOperationException("Coast propagation did not converge within its iteration limit.");
        var x = sign * candidate;
        var (C, S) = Stumpff(alpha * x * x);
        var f = 1 - x * x / radius * C;
        var g = dt - x * x * x / sqrtMu * S;
        var r = position * f + velocity * g;
        var length = r.Length;
        var fdot = sqrtMu / (length * radius) * (alpha * x * x * x * S - x);
        var gdot = 1 - x * x / length * C;
        var result = new CelestialState(r.Position, (position * fdot + velocity * gdot).Velocity);
        result.RequireFinite();
        return result;
    }

    internal static (double C, double S) Stumpff(double z)
    {
        if (Math.Abs(z) < 1e-4)
            return (.5 - z / 24 + z * z / 720 - z * z * z / 40320,
                1d / 6 - z / 120 + z * z / 5040 - z * z * z / 362880);
        if (z > 0)
        {
            var root = Math.Sqrt(z);
            return ((1 - Math.Cos(root)) / z, (root - Math.Sin(root)) / (root * root * root));
        }
        var negativeRoot = Math.Sqrt(-z);
        return ((Math.Cosh(negativeRoot) - 1) / -z,
            (Math.Sinh(negativeRoot) - negativeRoot) / (negativeRoot * negativeRoot * negativeRoot));
    }
}

internal static class LambertSolver
{
    // Universal-variable, zero-revolution solution of the two-position/time boundary problem.
    // The transfer plane is fixed by the endpoints. Degenerate endpoints need a separate solver.
    public static Velocity3 DepartureVelocity(Position3 departure, Position3 arrival, double seconds,
        double mu, bool longWay)
    {
        var r1 = Vector3d.From(departure); var r2 = Vector3d.From(arrival);
        var aRadius = r1.Length; var bRadius = r2.Length;
        if (aRadius <= 0 || bRadius <= 0) throw new ArgumentException("Transfer endpoints cannot be at the gravity centre.");
        var cosine = Math.Clamp(Vector3d.Dot(r1, r2) / (aRadius * bRadius), -1, 1);
        if (1 - Math.Abs(cosine) < 1e-10)
            throw new ArgumentException("Collinear transfer endpoints are degenerate; change the arrival epoch or waypoint.");
        var a = (longWay ? -1 : 1) * Math.Sqrt(aRadius * bRadius * (1 + cosine));
        double TimeAt(double z, out double y)
        {
            var (c, s) = TwoBodyTrajectory.Stumpff(z);
            y = aRadius + bRadius + a * (z * s - 1) / Math.Sqrt(c);
            if (y < 0) return -1; // Below the physical branch; bisection moves toward positive y.
            return (Math.Pow(y / c, 1.5) * s + a * Math.Sqrt(y)) / Math.Sqrt(mu);
        }
        var low = -4 * Math.PI * Math.PI;
        var high = 4 * Math.PI * Math.PI - 1e-6;
        for (var i = 0; i < 8 && TimeAt(low, out _) > seconds; i++) low *= 2;
        if (TimeAt(low, out _) > seconds || TimeAt(high, out _) < seconds)
            throw new ArgumentException("No zero-revolution transfer within the solver's time/range bounds.");
        var z = 0d;
        for (var i = 0; i < 120; i++)
        {
            z = (low + high) / 2;
            var time = TimeAt(z, out _);
            if (!double.IsFinite(time)) throw new ArgumentException("Transfer exceeds the supported numerical range.");
            if (time < seconds) low = z; else high = z;
        }
        var solvedTime = TimeAt(z, out var solvedY);
        if (solvedY <= 0 || Math.Abs(solvedTime - seconds) > Math.Max(1e-6, seconds * 1e-9))
            throw new ArgumentException("Transfer solver did not converge; change flight time or geometry.");
        var f = 1 - solvedY / aRadius;
        var g = a * Math.Sqrt(solvedY / mu);
        return ((r2 - r1 * f) / g).Velocity;
    }
}
