namespace Quasar.Simulation;

public sealed partial class SystemLab
{
    private long lastEventSequence;

    /// <summary>Capture a checkpoint on the simulation writer without changing time or movement.</summary>
    public SystemLabState ExportState() => new(movement.ExportState(), lastEventSequence,
        [.. events.Select(entry => new LabHistoryEntry(entry.Sequence, entry.AtTicks, entry.Kind, entry.ObjectId, entry.Message))]);

    /// <summary>Restore into a detached lab. The caller can adopt it after all checks pass.</summary>
    public static SystemLab RestoreState(SystemLabState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Simulation is null || state.History.IsDefaultOrEmpty || state.LastEventSequence != state.History.Length)
            throw new InvalidDataException("Missing simulation or inconsistent System Lab history sequence.");
        var movement = MovementExecution.RestoreState(state.Simulation);
        var ships = state.Simulation.Spacecraft;
        if (!movement.ContainsSpacecraft(ShipId) || ships.Any(ship =>
                ship.Id == ShipId ? ship.Sail is not null
                : ship.Id != SailcraftId || ship.Sail is null || movement.Catalogue.Bodies.ContainsKey(SailcraftId))
            || movement.Catalogue.Bodies.ContainsKey(ShipId)
            || !movement.Catalogue.Bodies.TryGetValue(
                movement.Catalogue.Bodies.ContainsKey(SolarSystem.Sun) ? SolarSystem.Sun : LegacyPrimaryId, out var primary)
            || primary.GravitationalParameterKm3PerSecond2 <= 0)
            throw new InvalidDataException("The System Lab requires Pathfinder and a supported gravity primary.");

        var rocketHistory = ValidateHistoryIdentities(state, movement);
        ValidatePathfinderHistory(state, movement, rocketHistory);
        if (movement.ContainsSpacecraft(SailcraftId))
            ValidateSailHistory(state.Simulation.ElapsedTicks, ships.Single(ship => ship.Id == SailcraftId),
                state.History.Where(entry => entry.ObjectId == SailcraftId));
        var restored = new SystemLab(movement) { lastEventSequence = state.LastEventSequence };
        restored.events.AddRange(state.History.Select(entry =>
            new LabEvent(entry.Sequence, entry.AtTicks, RestoredEventKind(entry), entry.ObjectId, entry.Message)));
        return restored;
    }

    private static LabHistoryEntry[] ValidateHistoryIdentities(SystemLabState state, MovementExecution movement)
    {
        var previousTicks = long.MinValue;
        for (var i = 0; i < state.History.Length; i++)
        {
            var entry = state.History[i];
            if (entry is null || entry.Sequence != i + 1L || entry.AtTicks < previousTicks
                || entry.AtTicks > state.Simulation.ElapsedTicks || entry.ObjectId is null
                || !movement.ContainsSpacecraft(entry.ObjectId)
                || string.IsNullOrWhiteSpace(entry.Message))
                throw new InvalidDataException("Invalid System Lab history entry.");
            previousTicks = entry.AtTicks;
        }
        if (state.History[0].ObjectId != ShipId)
            throw new InvalidDataException("System Lab history must begin with Pathfinder.");
        return [.. state.History.Where(entry => entry.ObjectId == ShipId)];
    }

    private static void ValidatePathfinderHistory(SystemLabState state, MovementExecution movement, LabHistoryEntry[] history)
    {
        var previousTicks = long.MinValue;
        var orderIndex = -1;
        var burning = false;
        for (var i = 0; i < history.Length; i++)
        {
            var entry = history[i];
            switch (entry.Kind)
            {
                case "created" when i == 0:
                    break;
                case "order" when i > 0 && !burning:
                    orderIndex = i;
                    break;
                case "patch" when orderIndex >= 0 && entry.AtTicks > previousTicks:
                case "primary-change" when state.Simulation.FormatVersion >= 7 && orderIndex >= 0 && entry.AtTicks >= previousTicks:
                    break;
                case "course-departure-cutoff" or "course-braking-start" when state.Simulation.FormatVersion >= 6
                    && orderIndex >= 0 && entry.AtTicks > previousTicks
                    && history.Take(i).Last(prior => prior.Kind != "primary-change").Kind
                        == (entry.Kind == "course-departure-cutoff" ? "order" : "course-departure-cutoff"):
                    break;
                case "course-arrival" or "course-approach-complete" or "course-orbit-inserted"
                    when state.Simulation.FormatVersion >= 6 && orderIndex >= 0 && entry.AtTicks > previousTicks
                    && history.Take(i).Last(prior => prior.Kind != "primary-change").Kind == "course-braking-start":
                case "arrival" when orderIndex >= 0 && entry.AtTicks > previousTicks:
                case "cancel" when orderIndex >= 0:
                    orderIndex = -1;
                    break;
                case "burn-start" when state.Simulation.FormatVersion >= 4 && i > 0 && orderIndex < 0 && !burning
                    && StartsFreeCoast(history[i - 1].Kind):
                    burning = true;
                    break;
                case "burn-complete" or "burn-exhausted" when burning && entry.AtTicks > previousTicks:
                case "burn-cutoff" when burning:
                    burning = false;
                    break;
                case "primary-change" when state.Simulation.FormatVersion >= 5 && i > 0 && orderIndex < 0 && !burning
                    && StartsFreeCoast(history[i - 1].Kind):
                case "refit" when state.Simulation.FormatVersion >= 6 && i > 0 && orderIndex < 0 && !burning
                    && StartsFreeCoast(history[i - 1].Kind):
                    break;
                default:
                    throw new InvalidDataException("Invalid System Lab event order.");
            }
            previousTicks = entry.AtTicks;
        }

        var ship = state.Simulation.Spacecraft.Single(ship => ship.Id == ShipId);
        var last = history[^1];
        if (burning != (ship.ActiveBurn is not null))
            throw new InvalidDataException("Active burn disagrees with System Lab history.");
        if (ship.ActiveBurn is { } burn)
        {
            if (last.AtTicks != burn.StartTicks)
                throw new InvalidDataException("Ignition epoch disagrees with System Lab history.");
        }
        else if (ship.FiniteCourse is { } finite)
        {
            var expected = movement.CommittedPlan(ShipId)!.Finite!.Boundaries
                .Where(boundary => boundary.Ticks <= state.Simulation.ElapsedTicks).ToArray();
            var count = expected.Length;
            if (orderIndex < 0 || history[orderIndex].AtTicks != finite.DepartureTicks
                || history.Length - orderIndex - 1 != count
                || expected.Where((boundary, index) => history[orderIndex + index + 1].Kind != boundary.Kind
                    || history[orderIndex + index + 1].AtTicks != boundary.Ticks).Any())
                throw new InvalidDataException("Finite course disagrees with System Lab history.");
        }
        else if (ship.Trajectory is { } trajectory)
        {
            if (orderIndex < 0 || trajectory.Destination.BodyId is null
                || history[orderIndex].AtTicks != trajectory.Segments[0].Coast.EpochTicks
                || history.Length - orderIndex - 1 != trajectory.NextBoundaryIndex)
                throw new InvalidDataException("Active trajectory disagrees with System Lab history.");
            for (var i = 0; i < trajectory.NextBoundaryIndex; i++)
                if (history[orderIndex + i + 1].AtTicks != trajectory.Segments[i].EndTicks)
                    throw new InvalidDataException("Consumed patch disagrees with System Lab history.");
        }
        else if (ship.Rendezvous is { } rendezvous)
        {
            if (rendezvous.Destination.BodyId is null || last.Kind != "arrival" || last.AtTicks != rendezvous.ArrivalTicks)
                throw new InvalidDataException("Rendezvous disagrees with System Lab history.");
        }
        else if (!StartsFreeCoast(last.Kind) || last.AtTicks != ship.FreeCoast!.EpochTicks)
            throw new InvalidDataException("Free coast disagrees with System Lab history.");
    }

    private static void ValidateSailHistory(long elapsedTicks, SpacecraftState ship, IEnumerable<LabHistoryEntry> history)
    {
        LabHistoryEntry? last = null;
        var sailing = false;
        foreach (var entry in history)
        {
            switch (entry.Kind)
            {
                case "sail-launched" when last is null:
                case "primary-change" when last is not null && !sailing:
                    break;
                case "sail-trim" when last is not null && !sailing:
                    sailing = true;
                    break;
                case "sail-complete" when sailing && entry.AtTicks > last!.AtTicks:
                case "sail-feathered" when sailing:
                    sailing = false;
                    break;
                default:
                    throw new InvalidDataException("Invalid sailcraft event order.");
            }
            last = entry;
        }
        var epoch = ship.ActiveSail?.StartTicks ?? ship.FreeCoast?.EpochTicks;
        if (last is null || last.AtTicks != epoch || epoch > elapsedTicks || sailing != (ship.ActiveSail is not null))
            throw new InvalidDataException("Sailcraft motion disagrees with System Lab history.");
    }

    private static bool StartsFreeCoast(string kind) => kind is "created" or "cancel" or "burn-complete"
        or "burn-exhausted" or "burn-cutoff" or "primary-change" or "course-arrival" or "course-approach-complete" or "course-orbit-inserted" or "refit";

    // The first v6 builds saved both outcomes under one kind. Their generated messages distinguish them.
    private static string RestoredEventKind(LabHistoryEntry entry) => (entry.Kind, entry.Message) switch
    {
        ("course-arrival", "Pathfinder completed braking and entered the requested orbit.") => "course-orbit-inserted",
        ("course-arrival", "Pathfinder completed braking at the approach point and resumed a gravitational coast.") => "course-approach-complete",
        _ => entry.Kind
    };
}
