using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.HealthScore.Models.Db;
using Bakabase.Modules.Search.Models.Db;

namespace Bakabase.Modules.HealthScore.Components;

/// <summary>
/// Computes a stable hash over score-affecting fields. Always operates on the
/// DB shape because StandardValue serialization is deterministic; the domain
/// shape uses <c>object?</c> property values which would round-trip
/// non-deterministically.
/// </summary>
/// <remarks>
/// Excluded from the hash (don't change a resource's score):
/// Name, Enabled, Priority, CreatedAt, UpdatedAt.
/// </remarks>
public static class HealthScoreProfileHasher
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Compute(HealthScoreProfileDbModel db)
    {
        var canonical = new
        {
            baseScore = db.BaseScore,
            membership = ParseMembership(db.MembershipFilterJson),
            rules = ParseRules(db.RulesJson),
        };

        var json = JsonSerializer.Serialize(canonical, Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static ResourceSearchFilterGroupDbModel? ParseMembership(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<ResourceSearchFilterGroupDbModel>(json, Options);

    private static List<HealthScoreRuleDbModel> ParseRules(string? json) =>
        string.IsNullOrEmpty(json)
            ? new()
            : JsonSerializer.Deserialize<List<HealthScoreRuleDbModel>>(json, Options) ?? new();
}
