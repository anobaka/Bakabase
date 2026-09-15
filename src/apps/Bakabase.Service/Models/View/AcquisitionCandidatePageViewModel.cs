using System.Collections.Generic;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.View;

namespace Bakabase.Service.Models.View;

/// <summary>Unmaterialized resources and their known routes, without probing or starting a download.</summary>
public record AcquisitionCandidatePageViewModel(
    List<AcquisitionCandidateViewModel> Items,
    int TotalCount,
    int Page,
    int PageSize,
    List<AcquisitionRecipeSummary> Recipes);

public record AcquisitionCandidateViewModel(
    int ResourceId,
    string ResourceName,
    List<AcquisitionCandidateLeadViewModel> Leads,
    int? ActiveTaskId,
    AcquisitionStatus? ActiveTaskStatus);

/// <summary>
/// A known route, not proof of ownership or availability. Platform identities are not checked
/// against the user's account here; shared links are not fetched. The value can be passed to
/// CreateAcquisition, including the Source:SourceKey form used by platform fetches.
/// </summary>
public record AcquisitionCandidateLeadViewModel(
    int Id,
    AcquisitionLeadKind Kind,
    string Value,
    string? SourceName,
    bool IsDerived,
    string? Note,
    string Availability,
    string Capability,
    string Method,
    string DefaultRecipeName,
    int? DefaultRecipeDefinitionId,
    List<int> ApplicableRecipeDefinitionIds)
{
    public Dictionary<int, WorkflowValidationResult> RecipeValidations { get; init; } = [];
}
