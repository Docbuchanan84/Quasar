using System.Collections.Frozen;

namespace Quasar.Simulation;

/// <summary>
/// An immutable simulation-owned catalogue with a bounded, thread-safe memo of one timestamp.
/// Queries change no physical state or clock. Replace catalogue data using WithBody; the new
/// service starts with an empty memo, including every affected descendant and derived quantity.
/// </summary>
public sealed class CelestialPositionService : ICelestialPositionService
{
    private readonly FrozenDictionary<string, CelestialBody> bodies;
    private readonly FrozenDictionary<string, KeplerianPropagator.CompiledOrbit> orbits;
    private readonly FrozenDictionary<string, OrbitalEnvironment> environments;
    private readonly string[] topologicalOrder;
    private readonly object gate = new();
    private readonly Dictionary<string, CelestialState> cache = new(StringComparer.Ordinal);
    private FrozenDictionary<string, CelestialState>? allStates;
    private long? cachedTicks;
    public IReadOnlyDictionary<string, CelestialBody> Bodies => bodies;

    public CelestialPositionService(IEnumerable<CelestialBody> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        bodies = definitions.ToFrozenDictionary(b => b.Id, StringComparer.Ordinal);
        var compiled = new Dictionary<string, KeplerianPropagator.CompiledOrbit>(StringComparer.Ordinal);
        var metadata = new Dictionary<string, OrbitalEnvironment>(StringComparer.Ordinal);
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var roots = new Queue<string>();
        foreach (var body in bodies.Values)
        {
            if (string.IsNullOrWhiteSpace(body.Id) || string.IsNullOrWhiteSpace(body.Name) || !Enum.IsDefined(body.Kind)
                || !double.IsFinite(body.RadiusKm) || body.RadiusKm < 0
                || !double.IsFinite(body.GravitationalParameterKm3PerSecond2) || body.GravitationalParameterKm3PerSecond2 < 0)
                throw new ArgumentException("Bodies require an identity, name, kind, and finite nonnegative GM.", nameof(definitions));
            body.ReferenceState.RequireFinite();
            if (body.ParentId is null)
            {
                if (body.Orbit is not null) throw new ArgumentException("An orbit requires a parent.", nameof(definitions));
                roots.Enqueue(body.Id);
            }
            else
            {
                if (!bodies.ContainsKey(body.ParentId)) throw new ArgumentException("Unknown orbital parent.", nameof(definitions));
                if (!children.TryGetValue(body.ParentId, out var descendants)) children[body.ParentId] = descendants = [];
                descendants.Add(body.Id);
            }
            double? period = null, relativeMu = null, soi = null;
            if (body.Orbit is { } orbit)
            {
                var parentMu = bodies[body.ParentId!].GravitationalParameterKm3PerSecond2;
                if (parentMu <= 0) throw new ArgumentException("An orbital parent needs positive GM.", nameof(definitions));
                relativeMu = parentMu + body.GravitationalParameterKm3PerSecond2;
                var ellipse = KeplerianPropagator.Compile(orbit, relativeMu.Value);
                compiled.Add(body.Id, ellipse);
                period = ellipse.PeriodSeconds;
                soi = orbit.SemiMajorAxisKm * Math.Pow(body.GravitationalParameterKm3PerSecond2 / parentMu, .4);
                if (!double.IsFinite(soi.Value)) throw new ArgumentException("SOI exceeds finite range.", nameof(definitions));
            }
            metadata.Add(body.Id, new(body.GravitationalParameterKm3PerSecond2, relativeMu, period, soi));
        }
        var ordered = new List<string>(bodies.Count);
        while (roots.TryDequeue(out var id))
        {
            ordered.Add(id);
            if (children.TryGetValue(id, out var descendants)) foreach (var child in descendants) roots.Enqueue(child);
        }
        if (ordered.Count != bodies.Count) throw new ArgumentException("Orbital hierarchy contains a cycle.", nameof(definitions));
        topologicalOrder = [.. ordered];
        orbits = compiled.ToFrozenDictionary(StringComparer.Ordinal);
        environments = metadata.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public CelestialPositionService WithBody(CelestialBody replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!bodies.ContainsKey(replacement.Id)) throw new KeyNotFoundException(replacement.Id);
        return new(bodies.Values.Select(body => body.Id == replacement.Id ? replacement : body));
    }

    public OrbitalEnvironment GetEnvironment(string bodyId) => environments[bodyId];

    public CelestialState GetState(string bodyId, long atTicks)
    {
        lock (gate)
        {
            if (!bodies.ContainsKey(bodyId)) throw new KeyNotFoundException(bodyId);
            PrepareCache(atTicks);
            if (cache.TryGetValue(bodyId, out var existing)) return existing;
            // Iterative ancestor walk avoids call-stack limits even for unusually deep hierarchies.
            var missing = new Stack<string>();
            string? current = bodyId;
            while (current is not null && !cache.ContainsKey(current))
            {
                missing.Push(current);
                current = bodies[current].ParentId;
            }
            while (missing.TryPop(out var id)) Evaluate(id, atTicks);
            return cache[bodyId];
        }
    }

    public IReadOnlyDictionary<string, CelestialState> GetStates(long atTicks)
    {
        lock (gate)
        {
            PrepareCache(atTicks);
            if (allStates is not null) return allStates;
            foreach (var id in topologicalOrder) if (!cache.ContainsKey(id)) Evaluate(id, atTicks);
            return allStates = cache.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }

    private void PrepareCache(long atTicks)
    {
        if (cachedTicks == atTicks) return;
        cache.Clear();
        allStates = null;
        cachedTicks = atTicks;
    }

    private void Evaluate(string id, long atTicks)
    {
        var body = bodies[id];
        CelestialState relative;
        if (orbits.TryGetValue(id, out var orbit)) relative = orbit.Calculate(atTicks);
        else
        {
            var seconds = (double)(((decimal)atTicks - body.ReferenceEpochTicks) / TimeSpan.TicksPerSecond);
            var state = body.ReferenceState;
            var v = state.VelocityKmPerSecond;
            relative = new(new(state.PositionKm.X + v.X * seconds, state.PositionKm.Y + v.Y * seconds,
                state.PositionKm.Z + v.Z * seconds), v);
        }
        var parent = body.ParentId is null ? default : cache[body.ParentId];
        var position = relative.PositionKm;
        var velocity = relative.VelocityKmPerSecond;
        var result = new CelestialState(new(parent.PositionKm.X + position.X, parent.PositionKm.Y + position.Y,
            parent.PositionKm.Z + position.Z), new(parent.VelocityKmPerSecond.X + velocity.X,
            parent.VelocityKmPerSecond.Y + velocity.Y, parent.VelocityKmPerSecond.Z + velocity.Z));
        result.RequireFinite();
        cache.Add(id, result);
    }
}
