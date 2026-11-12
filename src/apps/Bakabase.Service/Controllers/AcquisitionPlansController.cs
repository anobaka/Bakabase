using System;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/plans")]
public sealed class AcquisitionPlansController(AcquisitionPlanService service, IMediaPathGuard pathGuard,
    AcquisitionDeliveryDirectoryResolver? deliveryDirectories = null, AcquisitionOutputDirectoryResolver? outputDirectories = null) : ControllerBase
{
    [HttpGet("builders")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionPlanBuilders")]
    public ListResponse<AcquisitionPlanBuilderDescriptor> Builders() => new(service.Builders);

    [HttpPost("drafts")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "CreateAcquisitionDraft")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> CreateDraft([FromBody] AcquisitionDraftCreationInput input) =>
        Respond(async () => {await GuardNewDirectories(input.OutputDirectory, input.DeliverySettings); await GuardDirectory(input.StoragePolicy?.ProcessingWorkspaceDirectory); return await service.CreateDraftAsync(input, HttpContext.RequestAborted);});

    [HttpPut("{id:int}/draft")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "UpdateAcquisitionDraft")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> UpdateDraft(int id, [FromBody] AcquisitionDraftUpdateInput input) =>
        Respond(async () => {await GuardSavedDirectory(id); await GuardDirectory(input.OutputDirectory); await GuardDirectory(input.DeliverySettings?.RootDirectory); await GuardDirectory(input.StoragePolicy?.ProcessingWorkspaceDirectory); return await service.UpdateDraftAsync(id, input, HttpContext.RequestAborted);});

    [HttpPut("{id:int}/builder")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "SetAcquisitionDraftBuilder")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> SetBuilder(int id, [FromBody] AcquisitionDraftBuilderInput input) =>
        Respond(async () => {await GuardSavedDirectory(id); return await service.SetDraftBuilderAsync(id, input, HttpContext.RequestAborted);});

    [HttpPost("{id:int}/prepare")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PrepareAcquisitionDraft")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> PrepareDraft(int id, [FromBody] AcquisitionDraftPreparationInput input) =>
        Respond(async () => {await GuardSavedDirectory(id); await GuardDirectory(input.Plan?.OutputDirectory); return await service.PrepareDraftAsync(id, input, HttpContext.RequestAborted);});

    /// <summary>Explicitly preserves selected legacy overview entries. Never called by a list read.</summary>
    [HttpPost("legacy-overview/import")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ImportLegacyAcquisitionOverview")]
    public Task<SingletonResponse<AcquisitionDraftImportResult>> ImportLegacy([FromBody] AcquisitionLegacyImportInput input,
        [FromServices] AcquisitionDraftImportService importer) =>
        RespondImport(() => importer.ImportLegacyAsync(input, HttpContext.RequestAborted));

    [HttpPost("shared-list/import")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ImportAcquisitionDraftList")]
    public Task<SingletonResponse<AcquisitionDraftImportResult>> ImportList([FromBody] AcquisitionDraftListInput input,
        [FromServices] AcquisitionDraftImportService importer) =>
        RespondImport(() => importer.ImportListAsync(input, HttpContext.RequestAborted));

    [HttpPost]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "CreateAcquisitionPlanTask")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> Create([FromBody] AcquisitionPlanCreationInput input) =>
        Respond(async () => {await GuardNewDirectories(input.OutputDirectory, input.DeliverySettings); await GuardDirectory(input.StoragePolicy?.ProcessingWorkspaceDirectory); return await service.CreateAsync(input, HttpContext.RequestAborted);});

    [HttpGet("{id:int}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionPlanTask")]
    public async Task<SingletonResponse<AcquisitionPlanTaskDetail?>> Get(int id) =>
        new(await service.GetAsync(id, HttpContext.RequestAborted));

    [HttpPut("{id:int}/search")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "SearchAcquisitionPlanCandidates")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> Search(int id, [FromBody] AcquisitionPlanSearchInput input) =>
        Respond(async () => {await GuardSavedDirectory(id); return await service.SearchAsync(id, input.Query, HttpContext.RequestAborted);});

    [HttpPost("{id:int}/select")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "SelectAcquisitionPlanCandidate")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> Select(int id, [FromBody] AcquisitionPlanSelectionInput input) =>
        Respond(async () => {await GuardSavedDirectory(id); return await service.SelectAsync(id, input, HttpContext.RequestAborted);});

    [HttpPut("{id:int}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "UpdateAcquisitionPlanTask")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> Update(int id, [FromBody] AcquisitionPlanUpdateInput input) =>
        Respond(async () => {await GuardSavedDirectory(id); await GuardDirectory(input.OutputDirectory); await GuardDirectory(input.DeliverySettings?.RootDirectory); await GuardDirectory(input.StoragePolicy?.ProcessingWorkspaceDirectory);
            foreach (var entry in input.StoragePolicyOverrides ?? []) await GuardDirectory(entry.Policy?.ProcessingWorkspaceDirectory); return await service.UpdateAsync(id, input, HttpContext.RequestAborted);});

    [HttpPost("{id:int}/processing-revisions/apply")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ApplyAcquisitionProcessingRevisions")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> ApplyProcessingRevisions(int id,
        [FromBody] ApplyProcessingPlanRevisionsInput input) =>
        Respond(async () => {await GuardSavedDirectory(id); return await service.ApplyProcessingRevisionsAsync(id, input, HttpContext.RequestAborted);});

    [HttpPost("{id:int}/run")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "RunAcquisitionPlan")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> Run(int id, [FromBody] AcquisitionPlanRunInput input) =>
        Respond(async () => {await GuardSavedDirectory(id); return await service.RunAsync(id, input.ExpectedRevision, HttpContext.RequestAborted);});

    [HttpPost("{id:int}/replan")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ReplanAcquisitionTask")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> Replan(int id, [FromBody] AcquisitionPlanReplanInput input) =>
        Respond(async () => {await GuardSavedDirectory(id); return await service.ReplanAsync(id, input, HttpContext.RequestAborted, GuardDirectory);});

    [HttpPost("{id:int}/preflight")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreflightAcquisitionPlan")]
    public Task<SingletonResponse<AcquisitionPlanTaskDetail>> Preflight(int id) =>
        Respond(async () => {await GuardSavedDirectory(id); return await service.PreflightAsync(id, HttpContext.RequestAborted);});

    [HttpPost("by-sources")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionPlansBySources")]
    public async Task<ListResponse<AcquisitionPlanSourceTasks>> BySources([FromBody] AcquisitionPlanSourcesInput input)
    {
        try {return new(await service.BySourcesAsync(input.Sources, HttpContext.RequestAborted));}
        catch (ArgumentException error) {return ListResponseBuilder<AcquisitionPlanSourceTasks>.BuildBadRequest(error.Message);}
    }

    // Body and persisted paths use the same remote policy as scalar PathParameters. Empty
    // preparation input is allowed because no filesystem operation starts until confirmation.
    private async Task GuardDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var remote = HttpContext.GetRemoteAccessContext();
        if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
        if (!await pathGuard.IsServableAsync(path, HttpContext.RequestAborted))
            throw new UnauthorizedAccessException("This directory is outside the media libraries Bakabase serves.");
    }
    private async Task GuardSavedDirectory(int id)
    {
        var detail = await service.GetAsync(id, HttpContext.RequestAborted);
        await GuardDirectory(detail?.Plan?.OutputDirectory ?? detail?.Task.TargetDirectory);
        await GuardDirectory(detail?.DeliverySettings?.RootDirectory);
        await GuardDirectory(detail?.StoragePolicy?.ProcessingWorkspaceDirectory);
        foreach (var part in detail?.Plan?.Parts ?? [])
        {
            await GuardDirectory(part.StoragePolicy?.ProcessingWorkspaceDirectory);
            foreach (var step in part.Processing?.Steps ?? []) await GuardDirectory(step.StoragePolicy?.ProcessingWorkspaceDirectory);
            foreach (var source in part.Sources)
                foreach (var step in source.Processing?.Steps ?? []) await GuardDirectory(step.StoragePolicy?.ProcessingWorkspaceDirectory);
        }
        await GuardDirectory(detail?.Plan?.DeliveryLayout?.RootDirectory);
        await GuardDirectory(detail?.Plan?.DeliveryLayout?.TaskDirectory);
        if (detail?.Plan?.DeliveryLayout != null)
            foreach (var directory in detail.Plan.DeliveryLayout.PartDirectories.Values) await GuardDirectory(directory);
    }
    private async Task GuardNewDirectories(string? output, AcquisitionDeliverySettings? input)
    {
        var resolvedOutput = outputDirectories?.ResolveNew(output) ?? output;
        await GuardDirectory(resolvedOutput);
        var settings = deliveryDirectories?.Capture(input) ?? input;
        await GuardDirectory(settings?.RootDirectory);
    }

    private static async Task<SingletonResponse<AcquisitionPlanTaskDetail>> Respond(Func<Task<AcquisitionPlanTaskDetail>> operation)
    {
        try {return new(await operation());}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {return SingletonResponseBuilder<AcquisitionPlanTaskDetail>.BuildBadRequest(error.Message);}
    }

    private static async Task<SingletonResponse<AcquisitionDraftImportResult>> RespondImport(Func<Task<AcquisitionDraftImportResult>> operation)
    {
        try {return new(await operation());}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {return SingletonResponseBuilder<AcquisitionDraftImportResult>.BuildBadRequest(error.Message);}
    }
}
