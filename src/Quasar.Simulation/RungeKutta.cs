namespace Quasar.Simulation;

internal static class RungeKutta
{
    internal static void Step(ref Vector3d position, ref Vector3d velocity, double time, double step,
        Func<Vector3d, Vector3d, double, Vector3d> acceleration)
    {
        var half = step / 2;
        var a1 = acceleration(position, velocity, time);
        var v2 = velocity + a1 * half;
        var a2 = acceleration(position + velocity * half, v2, time + half);
        var v3 = velocity + a2 * half;
        var a3 = acceleration(position + v2 * half, v3, time + half);
        var v4 = velocity + a3 * step;
        var a4 = acceleration(position + v3 * step, v4, time + step);
        position += (velocity + v2 * 2 + v3 * 2 + v4) * (step / 6);
        velocity += (a1 + a2 * 2 + a3 * 2 + a4) * (step / 6);
    }
}
