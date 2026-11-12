using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record CloudDownloadStartInput
{
    public List<CloudSource> Sources { get; init; } = [];
    public string? Title { get; init; }
    public string? AccountId { get; init; }
    [Required] public string OutputDirectory { get; init; } = "";
    public int? WorkflowDefinitionId { get; init; }
}
public sealed record CloudDownloadPostInput
{
    [Range(1, int.MaxValue)] public int TaskId { get; init; }
    public int Revision { get; init; }
    public List<int> ResourceIndices { get; init; } = [];
    public string? AccountId { get; init; }
    [Required] public string OutputDirectory { get; init; } = "";
    public int? WorkflowDefinitionId { get; init; }
}
public sealed record CloudProviderView(string ProviderId, bool Available, string? Problem, bool SupportsTransfer, string? MethodId = null);

[ApiController]
[Route("~/cloud-downloads")]
public sealed class CloudDownloadsController(BakabaseDbContext db, IEnumerable<ICloudDownloadProvider> providers,
    IWorkflowDefinitionService workflows, IUserStoragePolicy storage, PostParserTaskExecutionGate postGate,
    PostParserWorkflowService<BakabaseDbContext> postWorkflow) : ControllerBase
{
    [HttpGet("providers")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetCloudDownloadProviders")]
    public async Task<SingletonResponse<List<CloudProviderView>>> Providers(CancellationToken ct)
    {
        var result = new List<CloudProviderView>();
        foreach (var provider in providers)
        {
            var ready = await provider.CheckReadyAsync(null, ct);
            result.Add(new(provider.ProviderId, ready.Available, ready.Problem, provider.SupportsTransfer, provider.MethodId));
        }
        return new(result);
    }

    [HttpPost("run")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "StartCloudDownload")]
    public Task<SingletonResponse<WorkflowRunViewModel>> Start([FromQuery] string outputDirectory, [FromBody] CloudDownloadStartInput input, CancellationToken ct) => Change(async () =>
    {
        var sources = input.Sources.Select((s, index) => Normalize(s with {Id = CloudOperationService.Key(index.ToString(), s.Url, s.AccessCode ?? "")})).ToList();
        var workset = CloudWorkflow.FromSources(sources, input.Title, Guid.NewGuid().ToString("N"), outputDirectory);
        return await Start(workset, input.AccountId, input.WorkflowDefinitionId, ct);
    });

    [HttpPost("from-post")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "StartCloudDownloadFromPost")]
    public Task<SingletonResponse<WorkflowRunViewModel>> FromPost([FromQuery] string outputDirectory, [FromBody] CloudDownloadPostInput input, CancellationToken ct) => Change(async () =>
    {
        await postGate.Semaphore.WaitAsync(ct);
        try
        {
            await postWorkflow.RefreshTaskUnderGateAsync(input.TaskId, ct);
            var task = await db.PostParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == input.TaskId && !t.IsDeleted, ct)
                ?? throw new ArgumentException("The saved parsing result no longer exists.");
            if (task.Revision != input.Revision) throw new InvalidOperationException("The post was parsed again. Refresh its results before starting a download.");
            if (task.ParsingState != "complete" || task.Error != null || string.IsNullOrWhiteSpace(task.Results))
                throw new ArgumentException("Complete parsing before downloading this result.");
            var resources = PostDownloadGroupNormalizer.Normalize(PostParserSavedResults.ReadDownloadInfo(task)).Resources;
            if (input.ResourceIndices.Count is 0 or > 500 || input.ResourceIndices.Any(i => i < 0 || i >= resources.Count))
                throw new ArgumentException("Select download sources from the current result.");
            var parsed = new Bakabase.Modules.PostParser.Models.Domain.PostDownloadInfo
                {Title = task.Title, Resources = input.ResourceIndices.Distinct().Select(i => resources[i]).ToList()};
            var workset = CloudWorkflow.Prepare(parsed, "post:" + task.Id + ":" + task.Revision + ":" + Guid.NewGuid().ToString("N"), outputDirectory)
                with {PostTaskId = task.Id, PostRevision = task.Revision,
                    SourceRefs = [new("postParser", "task", task.Id.ToString(), task.Revision.ToString())]};
            foreach (var target in workset.Targets) foreach (var source in target.Sources) Normalize(source);
            return await Start(workset, input.AccountId, input.WorkflowDefinitionId, ct);
        }
        finally {postGate.Semaphore.Release();}
    });

    private Task<WorkflowRunViewModel> Start(DownloadWorkset item, string? accountId, int? definitionId, CancellationToken ct) =>
        new Bakabase.Service.Components.DownloadIntents.DownloadIntentStarter(db, workflows, storage, HttpContext.RequestServices)
        .StartFilesAsync(item, accountId, definitionId, ct);

    internal static CloudSource Normalize(CloudSource source)
    {
        return Components.DownloadIntents.DownloadSourceResolver.Normalize(new()
        {
            Id = source.Id, Url = source.Url, Kind = source.Kind, ProviderId = source.Kind == "remote" ? source.ProviderId : null,
            MethodId = source.MethodId, AccountId = source.AccountId, AccessCode = source.AccessCode,
            ArchivePassword = source.ArchivePassword, ExtractionPlanJson = source.ExtractionPlanJson,
            GroupId = source.GroupId, Version = source.Version
        });
    }

    private static async Task<SingletonResponse<WorkflowRunViewModel>> Change(Func<Task<WorkflowRunViewModel>> action)
    {
        try {return new(await action());}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {return SingletonResponseBuilder<WorkflowRunViewModel>.BuildBadRequest(error.Message);}
    }
}
