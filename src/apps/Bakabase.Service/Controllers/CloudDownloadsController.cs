using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition;
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
public sealed record CloudProviderView(string ProviderId, bool Available, string? Problem, bool SupportsTransfer);

[ApiController]
[Route("~/cloud-downloads")]
public sealed class CloudDownloadsController(BakabaseDbContext db, IEnumerable<ICloudDownloadProvider> providers,
    IWorkflowDefinitionService workflows, IUserStoragePolicy storage, PostParserTaskExecutionGate postGate) : ControllerBase
{
    private static readonly SemaphoreSlim SeedGate = new(1, 1);
    [HttpGet("providers")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetCloudDownloadProviders")]
    public async Task<SingletonResponse<List<CloudProviderView>>> Providers(CancellationToken ct)
    {
        var result = new List<CloudProviderView>();
        foreach (var provider in providers)
        {
            var ready = await provider.CheckReadyAsync(null, ct);
            result.Add(new(provider.ProviderId, ready.Available, ready.Problem, provider.SupportsTransfer));
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
            var task = await db.PostParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == input.TaskId && !t.IsDeleted, ct)
                ?? throw new ArgumentException("The saved parsing result no longer exists.");
            if (task.Revision != input.Revision) throw new InvalidOperationException("The post was parsed again. Refresh its results before starting a download.");
            if (task.ParsingState != "complete" || task.Error != null || string.IsNullOrWhiteSpace(task.Results))
                throw new ArgumentException("Complete parsing before downloading this result.");
            var resources = PostParserAcquisitionService.ReadResources(task.Results);
            if (input.ResourceIndices.Count is 0 or > 500 || input.ResourceIndices.Any(i => i < 0 || i >= resources.Count))
                throw new ArgumentException("Select download sources from the current result.");
            var parsed = new Bakabase.Modules.PostParser.Models.Domain.PostDownloadInfo
                {Title = task.Title, Resources = input.ResourceIndices.Distinct().Select(i => resources[i]).ToList()};
            var workset = CloudWorkflow.Prepare(parsed, "post:" + task.Id + ":" + task.Revision + ":" + Guid.NewGuid().ToString("N"), outputDirectory)
                with {PostTaskId = task.Id, PostRevision = task.Revision};
            foreach (var target in workset.Targets) foreach (var source in target.Sources) Normalize(source);
            return await Start(workset, input.AccountId, input.WorkflowDefinitionId, ct);
        }
        finally {postGate.Semaphore.Release();}
    });

    private async Task<WorkflowRunViewModel> Start(DownloadWorkset item, string? accountId, int? definitionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.OutputDirectory)) throw new ArgumentException("Choose an output directory.");
        if (!System.IO.Path.IsPathFullyQualified(item.OutputDirectory)) throw new ArgumentException("Choose an absolute download directory.");
        storage.EnsurePathAllowed(item.OutputDirectory);
        if (item.Targets.Any(t => t.Sources.Any(s => s.ProviderId == "baidu")))
            accountId = await HttpContext.RequestServices.GetRequiredService<Bakabase.Service.Components.CloudDownloads.Baidu.BaiduPcsAccountStore>()
                .ResolveAccountIdAsync(accountId, ct);
        // The submitted sources are an explicit user selection. Mirrors still require one choice per group.
        if (item.Targets.Any(t => t.Sources.Count > 1)) throw new ArgumentException("Choose one mirror per content group.");
        item = item with {Targets = item.Targets.Select(t => t with {SelectedSourceId = t.Sources[0].Id, AccountId = t.Sources[0].ProviderId == "baidu" ? accountId : null,
            RemoteEntry = t.Sources[0].Kind == "remote" && accountId != null ? new("baidu", accountId, t.Sources[0].Url) : null}).ToList()};
        if (definitionId == null)
        {
            await SeedGate.WaitAsync(ct);
            try
            {
                var builtin = await db.Set<WorkflowDefinitionDbModel>().SingleOrDefaultAsync(d => d.Name == CloudWorkflow.BuiltinName && d.IsBuiltin, ct);
                if (builtin == null)
                {
                    var created = await workflows.CreateAsync(new WorkflowDefinitionCreationInputModel
                    {Name = CloudWorkflow.BuiltinName, TriggerKind = CloudWorkflow.Trigger, Enabled = true,
                        Activities = CloudWorkflow.Nodes.Select(k => new WorkflowActivityInputModel {Kind = k, ConfigJson = "{}"}).ToList()}, ct);
                    builtin = await db.Set<WorkflowDefinitionDbModel>().SingleAsync(d => d.Id == created.Id, ct);
                    builtin.IsBuiltin = true;
                    await db.SaveChangesAsync(ct);
                }
                definitionId = builtin.Id;
            }
            finally {SeedGate.Release();}
        }
        var definition = await workflows.GetAsync(definitionId.Value) ?? throw new ArgumentException("Choose a download workflow.");
        if (definition.TriggerKind != CloudWorkflow.Trigger) throw new ArgumentException("This workflow does not accept download sources.");
        return WorkflowRunViewModel.From(await workflows.RunManagedAsync(definition.Id, item, ct));
    }

    internal static CloudSource Normalize(CloudSource source)
    {
        if (source.Url.Length is 0 or > 2048 || source.AccessCode?.Length > 512 || source.ArchivePassword?.Length > 2048)
            throw new ArgumentException("The source URL or access information is invalid.");
        if (source.Kind == "remote")
        {
            if (source.ProviderId != "baidu" || !source.Url.StartsWith('/') || source.Url.Contains('\0'))
                throw new ArgumentException("A Baidu remote source needs an absolute cloud path.");
            return source;
        }
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "magnet"))
            throw new ArgumentException("Choose an HTTP(S), magnet or supported cloud share URL.");
        var provider = CloudSourceClassifier.Classify(source.Url);
        if (source.Kind == "http" && provider is not ("unknown" or "http")) throw new ArgumentException("Use the provider adapter for a cloud share.");
        return source with {ProviderId = source.Kind == "http" ? "http" : provider,
            Kind = provider is "http" or "torrent" ? provider : source.Kind};
    }

    private static async Task<SingletonResponse<WorkflowRunViewModel>> Change(Func<Task<WorkflowRunViewModel>> action)
    {
        try {return new(await action());}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {return SingletonResponseBuilder<WorkflowRunViewModel>.BuildBadRequest(error.Message);}
    }
}
