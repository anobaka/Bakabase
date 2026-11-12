using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.FileProcessing;

internal static class ArtifactWorkflowBindings
{
    public static async Task BindAsync(WorkflowExecutionContext context, string slot, string operationId, CancellationToken ct)
    {
        // In-memory workflow hosts have no database. The production host always supplies it.
        if (context.Services.GetService<BakabaseDbContext>() is not { } db) return;
        if (!await new ArtifactOperationBindingService(db).BindAsync("workflow", context.RunId.ToString(), slot, operationId, ct))
            throw new InvalidOperationException("This workflow slot is already bound to another artifact operation.");
    }
}
