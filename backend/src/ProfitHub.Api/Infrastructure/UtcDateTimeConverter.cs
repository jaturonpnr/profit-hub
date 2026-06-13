using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfitHub.Api.Infrastructure;

/// <summary>
/// Serializes every <see cref="DateTime"/> as UTC ISO-8601 with a trailing 'Z', regardless of
/// its <see cref="DateTimeKind"/>. This makes wire output deterministic across database providers:
/// Npgsql reads timestamps back as Kind=Utc, SQLite (test harness) as Kind=Unspecified. Without
/// normalization the test harness would emit values without 'Z', diverging from production.
///
/// Normalization rule: Unspecified is *assumed* to already be UTC (the backend only ever stores
/// UTC), Local is converted to UTC; the result is formatted with the round-trip ("O") format which
/// yields a trailing 'Z' for UTC kinds.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return DateTime.Parse(s!, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToUtc(value).ToString("O", CultureInfo.InvariantCulture));
}

/// <summary>Nullable counterpart of <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var s = reader.GetString();
        if (string.IsNullOrEmpty(s)) return null;
        return DateTime.Parse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(UtcDateTimeConverter.ToUtc(value.Value).ToString("O", CultureInfo.InvariantCulture));
    }
}
