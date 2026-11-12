namespace Quasar.Simulation;

public sealed record FusionTorchDesign(double JetPowerGw, double ExhaustSpeedKmPerSecond, double NozzleDiameterM)
{
    public static FusionTorchDesign Stock { get; } = new(150, 1000, 1.5);
}

public sealed record FusionTorchPreview(double MaximumJetPowerGw, double DriveDryMassKg, double DriveLengthM,
    double ThrustN, PropulsionPerformance Performance);
public sealed record FusionTorchComparison(FusionTorchPreview Stock, FusionTorchPreview Draft);
public sealed record TorchBurnResult(double ReactionMassUsedKg, double VelocityChangeKmPerSecond);
public sealed record TorchBurnComparison(int DurationSeconds, TorchBurnResult Stock, TorchBurnResult Draft);

public static class FusionTorchDesigner
{
    internal const string ReactionMassId = "torch-test";

    public static FusionTorchComparison Preview(FusionTorchDesign design) =>
        new(Evaluate(FusionTorchDesign.Stock), Evaluate(design));

    private static FusionTorchPreview Evaluate(FusionTorchDesign design)
    {
        var engine = BuildEngine(design, out var maximumPower);
        var propulsion = SystemLab.ExplorationTorch() with { Engine = engine };
        return new(maximumPower, engine.DryMassKg, engine.LengthM, engine.ThrustN, propulsion.CalculatePerformance());
    }

    public static TorchBurnComparison CompareBurn(FusionTorchDesign design)
    {
        var stock = SystemLab.ExplorationTorch();
        var draft = stock with { Engine = BuildEngine(design, out _) };
        const int durationSeconds = 30;
        return new(durationSeconds, SystemLab.TestTorchBurn(stock, durationSeconds),
            SystemLab.TestTorchBurn(draft, durationSeconds));
    }

    internal static ChemicalEngine BuildEngine(FusionTorchDesign design, out double maximumPower)
    {
        CheckRange(design.JetPowerGw, 50, 300, "Jet power", "GW");
        CheckRange(design.ExhaustSpeedKmPerSecond, 600, 1600, "Exhaust speed", "km/s");
        CheckRange(design.NozzleDiameterM, 1, 2.5, "Nozzle diameter", "m");
        var diameterRatio = design.NozzleDiameterM / FusionTorchDesign.Stock.NozzleDiameterM;
        var areaRatio = diameterRatio * diameterRatio;
        maximumPower = Math.Min(300, 150 * areaRatio);
        if (design.JetPowerGw > maximumPower)
            throw new ArgumentException(FormattableString.Invariant(
                $"This nozzle handles up to {Math.Floor(maximumPower * 100) / 100:0.##} GW. Widen it or lower jet power."));

        var exhaustSpeed = design.ExhaustSpeedKmPerSecond * 1000;
        var thrust = 2 * design.JetPowerGw * 1e9 / exhaustSpeed;
        // The existing engine record carries realized propulsion values in campaign saves.
        return new(400 + 600 * areaRatio, 3, design.NozzleDiameterM, thrust, exhaustSpeed, ReactionMassId);
    }

    private static void CheckRange(double value, double minimum, double maximum, string label, string unit)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException(FormattableString.Invariant($"{label} must be between {minimum} and {maximum} {unit}."));
    }
}
