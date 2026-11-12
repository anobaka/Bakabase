using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Compatibility entry point for extraction plans produced by post parsing.</summary>
public static class PostExtractionPlanValidator
{
    public const string IncompletePlanWarning = "PROCESSING_PLAN_INCOMPLETE";

    /// <summary>Keep an incomplete AI proposal non-executable without discarding its resource.</summary>
    public static PostExtractionPlan? ValidateAnalysis(PostExtractionPlan? plan, out bool incomplete)
    {
        incomplete = false;
        if (plan == null) return null;
        var normalized = Normalize(plan, allowMissingParameters: true);
        // Missing information cannot excuse an unsafe value elsewhere in this chain. Validate
        // all supplied fields before discarding the whole executable chain, never just its suffix.
        incomplete = FileProcessingPlanValidator.HasMissingRequiredParameters(normalized);
        return incomplete ? normalized with {Requirement = "required", Steps = []} : normalized;
    }

    public static PostExtractionPlan? Validate(PostExtractionPlan? plan)
    {
        if (plan == null) return null;
        // Normalize only at the AI-input boundary. The generic validator remains strict,
        // and returning a new record keeps raw/cached answers and their steps unchanged.
        var normalized = Normalize(plan, allowMissingParameters: false);
        FileProcessingPlanValidator.Validate(normalized);
        return normalized;
    }

    private static PostExtractionPlan Normalize(PostExtractionPlan plan, bool allowMissingParameters) => plan with
        {
            Steps = plan.Steps?.Select(step => step is {Op: "renameExtension"} &&
                !(allowMissingParameters && string.IsNullOrEmpty(step.Extension))
                ? step with {Extension = FileProcessingExtensionNormalizer.Normalize(step.Extension)}
                : step).ToList()!
        };
}
