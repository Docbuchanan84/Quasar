using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quasar.Simulation;

/// <summary>The UTF-8 checkpoint representation for the System Lab. Restore validates the decoded physics.</summary>
public static class SimulationStateJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters = { new Int64StringConverter(), new JsonStringEnumConverter<CelestialBodyKind>(JsonNamingPolicy.CamelCase, false) }
    };

    public static byte[] Serialize(SystemLabState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Simulation.RequireSupportedVersion();
        return JsonSerializer.SerializeToUtf8Bytes(state, Options);
    }

    public static SystemLabState Deserialize(ReadOnlySpan<byte> payload)
    {
        try
        {
            var state = JsonSerializer.Deserialize<SystemLabState>(payload, Options)
                ?? throw new InvalidDataException("Missing System Lab state.");
            state.Simulation.RequireSupportedVersion();
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Malformed System Lab state JSON.", exception);
        }
    }

    private sealed class Int64StringConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } value
                && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
                && value == parsed.ToString(CultureInfo.InvariantCulture))
                return parsed;
            throw new JsonException("Expected a signed Int64 decimal string.");
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
