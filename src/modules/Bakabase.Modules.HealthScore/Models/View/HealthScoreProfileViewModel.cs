using System.Text.Json;
using Bakabase.Modules.HealthScore.Models.Db;
using Bakabase.Modules.Search.Models.Db;

namespace Bakabase.Modules.HealthScore.Models.View;

/// <summary>
/// View shape mirrors the DB / Input shapes so frontend can round-trip GET → edit
/// → PATCH without ever needing to convert property values between typed objects
/// and StandardValue strings — the strings come down and go back up unchanged.
/// </summary>
public class HealthScoreProfileViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public decimal BaseScore { get; set; }
    public ResourceSearchFilterGroupDbModel? MembershipFilter { get; set; }
    public List<HealthScoreRuleDbModel> Rules { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Resource count from the most recent scoring run. Null when the profile
    /// has not been scored since startup (in-memory only, not persisted).
    /// </summary>
    public int? LastMatchedResourceCount { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static HealthScoreProfileViewModel From(HealthScoreProfileDbModel db, int? lastMatchedResourceCount = null) => new()
    {
        Id = db.Id,
        Name = db.Name,
        Enabled = db.Enabled,
        Priority = db.Priority,
        BaseScore = db.BaseScore,
        MembershipFilter = string.IsNullOrEmpty(db.MembershipFilterJson)
            ? null
            : JsonSerializer.Deserialize<ResourceSearchFilterGroupDbModel>(db.MembershipFilterJson, JsonOptions),
        Rules = string.IsNullOrEmpty(db.RulesJson)
            ? new()
            : JsonSerializer.Deserialize<List<HealthScoreRuleDbModel>>(db.RulesJson, JsonOptions) ?? new(),
        CreatedAt = db.CreatedAt,
        UpdatedAt = db.UpdatedAt,
        LastMatchedResourceCount = lastMatchedResourceCount,
    };
}
