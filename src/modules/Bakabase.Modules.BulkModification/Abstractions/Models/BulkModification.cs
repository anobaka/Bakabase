using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.BulkModification.Abstractions.Models;

public record BulkModification
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<BulkModificationVariable>? Variables { get; set; }
    /// <summary>
    /// 搜索条件（复用 ResourceSearch 领域模型）
    /// </summary>
    public ResourceSearch? Search { get; set; }
    public List<BulkModificationProcess>? Processes { get; set; }
    /// <summary>
    /// Scope priority overrides applied to every filtered resource on Apply.
    /// Each entry targets one (PropertyPool, PropertyId); empty Priorities clears the preference.
    /// </summary>
    public List<PropertyValueScopePreference>? ScopePreferenceConfigs { get; set; }
    public bool DeleteResources { get; set; }
    public bool DeleteFiles { get; set; }
    public List<int>? FilteredResourceIds { get; set; }
    public DateTime? AppliedAt { get; set; }
    public int ResourceDiffCount { get; set; }
}
