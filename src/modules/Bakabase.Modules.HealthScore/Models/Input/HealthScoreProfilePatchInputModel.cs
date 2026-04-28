using Bakabase.Modules.Search.Models.Db;

namespace Bakabase.Modules.HealthScore.Models.Input;

public class HealthScoreProfilePatchInputModel
{
    public string? Name { get; set; }
    public bool? Enabled { get; set; }
    public int? Priority { get; set; }
    public decimal? BaseScore { get; set; }

    /// <summary>Membership filter in DB shape (mirrors how BulkModification accepts <c>ResourceSearchInputModel</c>).</summary>
    public ResourceSearchFilterGroupDbModel? MembershipFilter { get; set; }

    /// <summary>Rules with property values pre-serialized as StandardValue strings.</summary>
    public List<HealthScoreRuleInputModel>? Rules { get; set; }
}
