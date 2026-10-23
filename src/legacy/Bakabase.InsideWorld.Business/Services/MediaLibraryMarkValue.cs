using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>One ledger row per mark/resource; retains compatibility with historical scalar library IDs.</summary>
internal static class MediaLibraryMarkValue
{
    public static List<int> Read(string? value)
    {
        if (int.TryParse(value, out var id)) return id > 0 ? [id] : [];
        if (string.IsNullOrWhiteSpace(value)) return [];
        try { return (JsonConvert.DeserializeObject<List<int>>(value) ?? []).Where(v => v > 0).Distinct().ToList(); }
        catch (JsonException) { return []; }
    }

    public static string Write(IReadOnlyList<int> ids) => ids.Count == 1
        ? ids[0].ToString()
        : JsonConvert.SerializeObject(ids);
}
