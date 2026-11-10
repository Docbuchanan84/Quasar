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
        if (state.Simulation.Spacecraft.Length != 1 || state.Simulation.Spacecraft[0].Id != ShipId
            || movement.Catalogue.Bodies.ContainsKey(ShipId)
            || !movement.Catalogue.Bodies.TryGetValue(
                movement.Catalogue.Bodies.ContainsKey(SolarSystem.Sun) ? SolarSystem.Sun : LegacyPrimaryId, out var primary)
            || primary.GravitationalParameterKm3PerSecond2 <= 0)
            throw new InvalidDataException("The System Lab requires Pathfinder and a supported gravity primary.");

        ValidateHistory(state);
        var restored = new SystemLab(movement) { lastEventSequence = state.LastEventSequence };
        restored.events.AddRange(state.History.Select(entry =>
            new LabEvent(entry.Sequence, entry.AtTicks, entry.Kind, entry.ObjectId, entry.Message)));
        return restored;
    }

    private static void ValidateHistory(SystemLabState state)
    {
        var previousTicks = long.MinValue;
        var orderIndex = -1;
        var burning = false;
        for (var i = 0; i < state.History.Length; i++)
        {
            var entry = state.History[i];
            if (entry is null || entry.Sequence != i + 1L || entry.AtTicks < previousTicks
                || entry.AtTicks > state.Simulation.ElapsedTicks || entry.ObjectId != ShipId
                || string.IsNullOrWhiteSpace(entry.Message))
                throw new InvalidDataException("Invalid System Lab history entry.");
            switch (entry.Kind)
            {
                case "created" when i == 0:
                    break;
                case "order" when i > 0 && !burning:
                    orderIndex = i;
                    break;
                case "patch" when orderIndex >= 0 && entry.AtTicks > previousTicks:
                    break;
                case "arrival" when orderIndex >= 0 && entry.AtTicks > previousTicks:
                case "cancel" when orderIndex >= 0:
                    orderIndex = -1;
                    break;
                case "burn-start" when state.Simulation.FormatVersion >= 4 && i > 0 && orderIndex < 0 && !burning
                    && state.History[i - 1].Kind is ("created" or "cancel" or "burn-complete" or "burn-exhausted" or "burn-cutoff"):
                    burning = true;
                    break;
                case "burn-complete" or "burn-exhausted" when burning && entry.AtTicks > previousTicks:
                case "burn-cutoff" when burning:
                    burning = false;
                    break;
                default:
                    throw new InvalidDataException("Invalid System Lab event order.");
            }
            previousTicks = entry.AtTicks;
        }

        var ship = state.Simulation.Spacecraft[0];
        var last = state.History[^1];
        if (burning != (ship.ActiveBurn is not null))
            throw new InvalidDataException("Active burn disagrees with System Lab history.");
        if (ship.ActiveBurn is { } burn)
        {
            if (last.AtTicks != burn.StartTicks)
                throw new InvalidDataException("Ignition epoch disagrees with System Lab history.");
        }
        else if (ship.Trajectory is { } trajectory)
        {
            if (orderIndex < 0 || trajectory.Destination.BodyId is null
                || state.History[orderIndex].AtTicks != trajectory.Segments[0].Coast.EpochTicks
                || state.History.Length - orderIndex - 1 != trajectory.NextBoundaryIndex)
                throw new InvalidDataException("Active trajectory disagrees with System Lab history.");
            for (var i = 0; i < trajectory.NextBoundaryIndex; i++)
                if (state.History[orderIndex + i + 1].AtTicks != trajectory.Segments[i].EndTicks)
                    throw new InvalidDataException("Consumed patch disagrees with System Lab history.");
        }
        else if (ship.Rendezvous is { } rendezvous)
        {
            if (rendezvous.Destination.BodyId is null || last.Kind != "arrival" || last.AtTicks != rendezvous.ArrivalTicks)
                throw new InvalidDataException("Rendezvous disagrees with System Lab history.");
        }
        else if (last.Kind is not ("created" or "cancel" or "burn-complete" or "burn-exhausted" or "burn-cutoff") || last.AtTicks != ship.FreeCoast!.EpochTicks)
            throw new InvalidDataException("Free coast disagrees with System Lab history.");
    }
}
