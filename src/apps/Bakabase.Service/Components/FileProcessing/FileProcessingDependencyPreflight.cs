using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Checks the tools required by explicit processing instructions, without installation or file work.</summary>
public static class FileProcessingDependencyPreflight
{
    public sealed record Issue(string Code, string ComponentId, string Message, string MessageKey);

    public static async Task<IReadOnlyList<Issue>> InspectAsync(IEnumerable<FileProcessingPlan?> plans,
        IEnumerable<IDependentComponentService> dependencies, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!plans.Any(p => p?.Steps.Any(s => s.Op == "extractArchive" && s.ZipPasswordCodePage != null) == true))
            return [];
        var engine = dependencies.SingleOrDefault(d => d.Id == PyzipperService.ComponentId);
        if (engine?.IsAvailableOnCurrentPlatform == true)
        {
            try
            {
                if (engine.Status is not (DependentComponentStatus.Installed or DependentComponentStatus.Installing))
                    await engine.Discover(ct);
                if (engine.Status == DependentComponentStatus.Installed) return [];
            }
            catch (System.Exception error) when (error is not System.OperationCanceledException)
            {
                // Do not echo a subprocess error or configuration into the validation response.
            }
        }
        return [new("DEPENDENCY_REQUIRED", PyzipperService.ComponentId,
            "Install or configure the ZIP compatibility engine (pyzipper).",
            "workflow.processing.zipPasswordCodePage.dependencyRequired")];
    }

    public static async Task<IReadOnlyList<WorkflowValidationIssue>> InspectWorkflowAsync(
        WorkflowValidationContext context, IEnumerable<FileProcessingPlan?> plans, CancellationToken ct) =>
        (await InspectAsync(plans, context.Services.GetServices<IDependentComponentService>(), ct))
        .Select(i => new WorkflowValidationIssue {Code = i.Code, Message = i.Message, MessageKey = i.MessageKey}).ToArray();
}
