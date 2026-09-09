namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

/// <summary>
/// One step of a recipe: which step, configured how.
/// </summary>
/// <param name="Kind">Resolved against the step registry when the recipe runs.</param>
/// <param name="ConfigJson">Shaped by that step's <c>ConfigType</c>; null when it takes none.</param>
public record AcquisitionRecipeStep(string Kind, string? ConfigJson = null);

/// <summary>
/// An ordered list of configured steps — the whole of "how to get this". A recipe is data, not
/// code: adding a way to obtain things means writing a recipe, not changing the engine.
/// </summary>
/// <param name="Name">Identifies a built-in recipe, and is what a copy is named after.</param>
/// <param name="Steps">Run in order.</param>
public record AcquisitionRecipe(string Name, IReadOnlyList<AcquisitionRecipeStep> Steps);
