using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Identity;

/// <summary>
/// Immutable per-entity version vector with VALUE equality. Missing actor = 0; stored counters are ≥ 1.
/// </summary>
[JsonConverter(typeof(DataSyncVersionVectorJsonConverter))]
public sealed class DataSyncVersionVector : IEquatable<DataSyncVersionVector>
{
    /// <summary>
    /// The largest counter a vector holds: 2^53, the largest integer every JSON reader (JavaScript included)
    /// keeps exact.
    /// </summary>
    internal const long MaxCounter = 1L << 53;

    private readonly ImmutableSortedDictionary<string, long> _counters;
    private string? _canonical;

    private DataSyncVersionVector(ImmutableSortedDictionary<string, long> counters) => _counters = counters;

    public static DataSyncVersionVector Empty { get; } =
        new(ImmutableSortedDictionary.Create<string, long>(StringComparer.Ordinal));

    /// <summary>Actor id → counter, keys ordinal-sorted.</summary>
    public IReadOnlyDictionary<string, long> Counters => _counters;

    public long this[DataSyncActorId actor] => _counters.TryGetValue(RequireActor(actor), out var counter) ? counter : 0;

    /// <summary>This vector with <paramref name="actor"/> at <paramref name="counter"/>; throws unless it increases.</summary>
    public DataSyncVersionVector With(DataSyncActorId actor, long counter)
    {
        var key = RequireActor(actor);
        var current = _counters.TryGetValue(key, out var existing) ? existing : 0;
        if (counter <= current)
            throw new ArgumentOutOfRangeException(nameof(counter), counter,
                $"A counter of actor {key} must exceed {current}.");
        if (counter > MaxCounter)
            throw new ArgumentOutOfRangeException(nameof(counter), counter, $"A counter must not exceed {MaxCounter}.");
        return new DataSyncVersionVector(_counters.SetItem(key, counter));
    }

    /// <summary>The per-actor maximum of both vectors.</summary>
    public static DataSyncVersionVector Max(DataSyncVersionVector a, DataSyncVersionVector b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (b._counters.Count == 0) return a;
        if (a._counters.Count == 0) return b;

        var builder = a._counters.ToBuilder();
        var changed = false;
        foreach (var (actor, counter) in b._counters)
        {
            if (builder.TryGetValue(actor, out var existing) && existing >= counter) continue;
            builder[actor] = counter;
            changed = true;
        }

        return changed ? new DataSyncVersionVector(builder.ToImmutable()) : a;
    }

    /// <summary>The relation of this vector to <paramref name="other"/>.</summary>
    public DataSyncVvRelation CompareTo(DataSyncVersionVector other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var thisAhead = false;
        var otherAhead = false;
        foreach (var (actor, counter) in _counters)
        {
            var theirs = other._counters.TryGetValue(actor, out var value) ? value : 0;
            if (counter > theirs) thisAhead = true;
            else if (counter < theirs) otherAhead = true;
        }

        // Stored counters are ≥ 1, so an actor only the other vector names puts it ahead.
        if (!otherAhead) otherAhead = other._counters.Keys.Any(actor => !_counters.ContainsKey(actor));

        return (thisAhead, otherAhead) switch
        {
            (false, false) => DataSyncVvRelation.Equal,
            (true, false) => DataSyncVvRelation.Dominates,
            (false, true) => DataSyncVvRelation.DominatedBy,
            _ => DataSyncVvRelation.Concurrent,
        };
    }

    /// <summary>Canonical JSON object {"&lt;actor&gt;":n,…}, keys sorted; the only stored and wire form.</summary>
    public string ToCanonicalString() => _canonical ??= BuildCanonicalString();

    /// <summary>
    /// Peer input: never throws; false for a non-object, a bad actor id, a counter &lt; 1 or &gt; 2^53, more than
    /// MaxActorsPerVector entries.
    /// </summary>
    public static bool TryParse(JsonNode? node, DataSyncLimits limits, out DataSyncVersionVector vv)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return TryRead(node, limits.MaxActorsPerVector, out vv, out _);
    }

    /// <summary>Local storage: throws InvalidDataException on corruption (a store bug, never peer input).</summary>
    public static DataSyncVersionVector ParseStored(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"A stored version vector is not JSON: {e.Message}", e);
        }

        return TryRead(node, null, out var vv, out var error)
            ? vv
            : throw new InvalidDataException($"A stored version vector is corrupted: {error}");
    }

    public bool Equals(DataSyncVersionVector? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || other._counters.Count != _counters.Count) return false;
        foreach (var (actor, counter) in _counters)
        {
            if (!other._counters.TryGetValue(actor, out var theirs) || theirs != counter) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is DataSyncVersionVector v && Equals(v);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToCanonicalString());

    public static bool operator ==(DataSyncVersionVector? a, DataSyncVersionVector? b) => a is null ? b is null : a.Equals(b);

    public static bool operator !=(DataSyncVersionVector? a, DataSyncVersionVector? b) => !(a == b);

    public override string ToString() => ToCanonicalString();

    /// <summary>
    /// Reads a vector without throwing. <paramref name="maxActors"/> null = no limit on the number of actors
    /// (local storage).
    /// </summary>
    internal static bool TryRead(JsonNode? node, int? maxActors, out DataSyncVersionVector vv, out string? error)
    {
        vv = Empty;
        try
        {
            if (node is not JsonObject obj)
            {
                error = "not a JSON object";
                return false;
            }

            if (maxActors is { } max && obj.Count > max)
            {
                error = $"more than {max} actors";
                return false;
            }

            var builder = ImmutableSortedDictionary.CreateBuilder<string, long>(StringComparer.Ordinal);
            foreach (var (actor, value) in obj)
            {
                if (!DataSyncActorId.IsValid(actor))
                {
                    error = $"invalid actor id '{actor}'";
                    return false;
                }

                if (value is not JsonValue number || !JsonNumbers.TryGetInt64(number, out var counter) ||
                    counter is < 1 or > MaxCounter)
                {
                    error = $"invalid counter for actor {actor}";
                    return false;
                }

                if (!builder.TryAdd(actor, counter))
                {
                    error = $"actor {actor} appears twice";
                    return false;
                }
            }

            vv = builder.Count == 0 ? Empty : new DataSyncVersionVector(builder.ToImmutable());
            error = null;
            return true;
        }
        catch (Exception e)
        {
            // A JsonObject parsed from text with duplicate member names throws when it is first read. Whatever a
            // malformed node throws, reading a vector never does.
            vv = Empty;
            error = e.Message;
            return false;
        }
    }

    private string BuildCanonicalString()
    {
        // Actor ids are lowercase hex and counters are integers, so nothing needs escaping; the ordinal key order
        // is CanonicalJson's (UTF-16 code units).
        var sb = new StringBuilder("{");
        var first = true;
        foreach (var (actor, counter) in _counters)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(actor).Append("\":").Append(counter.ToString(CultureInfo.InvariantCulture));
        }

        return sb.Append('}').ToString();
    }

    private static string RequireActor(DataSyncActorId actor) =>
        actor.Value ?? throw new ArgumentException("An uninitialized actor id cannot be used.", nameof(actor));
}

/// <summary>
/// System.Text.Json form of a vector: its canonical object. Reading validates like
/// <see cref="DataSyncVersionVector.ParseStored"/> (no actor limit) and reports corruption as a JsonException.
/// </summary>
internal sealed class DataSyncVersionVectorJsonConverter : JsonConverter<DataSyncVersionVector>
{
    public override DataSyncVersionVector Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        return DataSyncVersionVector.TryRead(node, null, out var vv, out var error)
            ? vv
            : throw new JsonException($"Invalid version vector: {error}");
    }

    public override void Write(Utf8JsonWriter writer, DataSyncVersionVector value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToCanonicalString(), skipInputValidation: true);
}
