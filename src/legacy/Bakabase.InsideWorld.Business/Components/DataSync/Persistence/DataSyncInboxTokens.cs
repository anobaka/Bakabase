using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// An inbox item's token (§9.2): the first 32 hex of SHA-256 over canonical
/// <c>{type, subjectPath, fields: [{path, base, local, remote}]}</c>. No record hash and no vector go into it, so an
/// unrelated peer edit never invalidates a card; usage counts and unrelated local changes stay out too. Used for the
/// items persistence derives from local state (<c>SuspectedLostUpdate</c>, §6.5).
/// </summary>
public static class DataSyncInboxTokens
{
    public static string Of(DataSyncInboxItemType type, string subjectPath, IEnumerable<DataSyncFieldOutcome> fields)
    {
        ArgumentNullException.ThrowIfNull(subjectPath);
        ArgumentNullException.ThrowIfNull(fields);
        var document = new JsonObject
        {
            ["fields"] = new JsonArray(fields.Select(f => (JsonNode?) new JsonObject
            {
                ["base"] = Display(f.Base),
                ["local"] = Display(f.Local),
                ["path"] = f.Path,
                ["remote"] = Display(f.Remote),
            }).ToArray()),
            ["subjectPath"] = subjectPath,
            ["type"] = type.ToString(),
        };
        var hash = SHA256.HashData(CanonicalJson.SerializeToUtf8Bytes(document));
        return Convert.ToHexStringLower(hash)[..32];
    }

    private static JsonNode? Display(DataSyncDisplayValue? value) =>
        value is null ? null : JsonSerializer.SerializeToNode(value, DataSyncJson.Options);
}
