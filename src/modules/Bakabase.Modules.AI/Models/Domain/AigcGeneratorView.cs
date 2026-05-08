using Bakabase.Modules.AI.Models.Db;

namespace Bakabase.Modules.AI.Models.Domain;

/// <summary>
/// Generator + its property presets, returned together to the UI.
/// </summary>
public record AigcGeneratorView
{
    public required AigcGeneratorDbModel Generator { get; init; }
    public required IReadOnlyList<AigcGeneratorPropertyPresetDbModel> PropertyPresets { get; init; }
}
