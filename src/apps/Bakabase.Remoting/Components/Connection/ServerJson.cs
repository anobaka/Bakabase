using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bakabase.Remoting.Components.Connection;

/// <summary>
/// How this client reads what a Bakabase server sends.
/// </summary>
/// <remarks>
/// <para>
/// One set of options rather than an identical copy beside every caller. The copies were
/// already identical; what they were missing, they were missing in unison, and nothing
/// would have told the next one to catch up.
/// </para>
/// <para>
/// The server serializes with Newtonsoft under
/// <c>DateFormatString = "yyyy-MM-dd HH:mm:ss.fff"</c> — a space where ISO 8601 puts a
/// <c>T</c>, and no offset at all. System.Text.Json reads only ISO 8601, so every
/// response carrying a timestamp threw before <see cref="ServerDateTimeConverter"/>
/// existed, and the throw surfaced as "not a Bakabase server". The converter is
/// registered here, once, for that reason.
/// </para>
/// </remarks>
public static class ServerJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            // The server writes enums as numbers; this reads both, so a server that ever
            // switches to names does not become unreadable.
            new JsonStringEnumConverter(),
            new ServerDateTimeConverter()
        }
    };
}

/// <summary>
/// Reads the timestamps a Bakabase server writes, and writes ISO 8601 back.
/// </summary>
/// <remarks>
/// Registered for <see cref="DateTime"/>; System.Text.Json routes <c>DateTime?</c>
/// through it too, handling the null itself.
/// <para>
/// Reading goes through <see cref="ServerClock.ParseServerTime"/> so there is exactly one
/// answer to "what does a server timestamp mean" — including the part that is easy to get
/// wrong twice: the server stamps <c>DateTime.UtcNow</c> and then drops the marker saying
/// so, so a reader that does not assume UTC is silently off by its own offset.
/// </para>
/// <para>
/// Writing is plain ISO 8601 with the offset present. Newtonsoft reads that without
/// complaint, so a request body this client sends needs no matching quirk.
/// </para>
/// </remarks>
public sealed class ServerDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        var parsed = ServerClock.ParseServerTime(raw);

        if (parsed == null)
        {
            throw new JsonException(
                $"'{raw}' is not a timestamp this client can read. Bakabase servers write " +
                "'yyyy-MM-dd HH:mm:ss.fff' in UTC, and ISO 8601 is accepted too.");
        }

        return parsed.Value;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}
