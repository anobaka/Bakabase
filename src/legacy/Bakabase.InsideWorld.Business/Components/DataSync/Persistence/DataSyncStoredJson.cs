using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// The JSON columns of the data sync tables (§4.1), written with <see cref="DataSyncJson.Options"/>. These are local
/// storage, never peer input: a value that does not parse is a store bug and throws
/// <see cref="InvalidDataException"/>, like <see cref="DataSyncVersionVector.ParseStored"/>.
/// </summary>
public static class DataSyncStoredJson
{
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, DataSyncJson.Options);

    public static T Read<T>(string json, string column)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, DataSyncJson.Options) ??
                   throw new InvalidDataException($"The stored {column} is null.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The stored {column} is corrupted: {e.Message}", e);
        }
    }

    public static IReadOnlyDictionary<string, long> ReadCounters(string? json, string column) =>
        string.IsNullOrEmpty(json)
            ? new Dictionary<string, long>(StringComparer.Ordinal)
            : new Dictionary<string, long>(Read<Dictionary<string, long>>(json, column), StringComparer.Ordinal);

    public static string WriteCounters(IReadOnlyDictionary<string, long> counters) =>
        Write(new SortedDictionary<string, long>(counters.ToDictionary(e => e.Key, e => e.Value),
            StringComparer.Ordinal));

    public static IReadOnlyDictionary<string, int> ReadVersions(string? json, string column) =>
        string.IsNullOrEmpty(json)
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(Read<Dictionary<string, int>>(json, column), StringComparer.Ordinal);

    public static string WriteVersions(IReadOnlyDictionary<string, int> versions) =>
        Write(new SortedDictionary<string, int>(versions.ToDictionary(e => e.Key, e => e.Value),
            StringComparer.Ordinal));

    public static IReadOnlyList<string> ReadStrings(string? json, string column) =>
        string.IsNullOrEmpty(json) ? [] : Read<List<string>>(json, column);

    public static IReadOnlyDictionary<string, string> ReadChildMap(string? json) =>
        string.IsNullOrEmpty(json)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(Read<Dictionary<string, string>>(json, "ChildMapJson"),
                StringComparer.Ordinal);

    public static string WriteChildMap(IReadOnlyDictionary<string, string> map) =>
        Write(new SortedDictionary<string, string>(map.ToDictionary(e => e.Key, e => e.Value), StringComparer.Ordinal));

    /// <summary>OverlayJson: null means <see cref="DataSyncOverlay.None"/>.</summary>
    public static DataSyncOverlay ReadOverlay(string? json) =>
        string.IsNullOrEmpty(json) ? DataSyncOverlay.None : Read<DataSyncOverlay>(json, "OverlayJson");

    /// <summary>An empty overlay is stored as null.</summary>
    public static string? WriteOverlay(DataSyncOverlay overlay) =>
        overlay.LocalOnlyChildren.Count == 0 && overlay.HeldChildren.Count == 0 ? null : Write(overlay);

    public static DataSyncMergeFlags ReadFlags(string? json, string column) =>
        string.IsNullOrEmpty(json) ? DataSyncMergeFlags.None : Read<DataSyncMergeFlags>(json, column);

    /// <summary>No flags are stored as null.</summary>
    public static string? WriteFlags(DataSyncMergeFlags flags) => flags == DataSyncMergeFlags.None ? null : Write(flags);

    public static DataSyncVersionVector? ReadVv(string? json) =>
        string.IsNullOrEmpty(json) ? null : DataSyncVersionVector.ParseStored(json);

    public static DataSyncWireRecord? ReadRecord(string? json, string column) =>
        string.IsNullOrEmpty(json) ? null : Read<DataSyncWireRecord>(json, column);

    /// <summary>Every key of a stored record, primary first, without parsing its content.</summary>
    public static IReadOnlyList<string> ReadRecordKeys(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        using var document = ParseDocument(json, "a record");
        return document.RootElement.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array
            ? keys.EnumerateArray().Select(k => k.GetString()!).ToArray()
            : [];
    }

    /// <summary>A stored record's vector, without materializing its content (retention scans every one).</summary>
    public static DataSyncVersionVector? ReadRecordVv(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        using var document = ParseDocument(json, "a record");
        return document.RootElement.TryGetProperty("vv", out var vv)
            ? DataSyncVersionVector.ParseStored(vv.GetRawText())
            : null;
    }

    private static JsonDocument ParseDocument(string json, string what)
    {
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions {MaxDepth = DataSyncJson.Options.MaxDepth});
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The stored JSON of {what} is corrupted: {e.Message}", e);
        }
    }

    public static EntityKeys ToEntityKeys(string primary, IEnumerable<string> aliases) =>
        new([new SyncKey(primary), ..aliases.Where(a => a != primary).Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal).Select(a => new SyncKey(a))]);
}
