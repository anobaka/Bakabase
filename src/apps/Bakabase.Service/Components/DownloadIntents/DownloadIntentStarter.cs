using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.DownloadIntents;

/// <summary>Both the legacy cloud endpoint and the generic intent endpoint use this single starter.</summary>
public sealed class DownloadIntentStarter(BakabaseDbContext db, IWorkflowDefinitionService workflows,
    IUserStoragePolicy storage, IServiceProvider services)
{
    private static readonly SemaphoreSlim SeedGate = new(1, 1);

    public async Task<WorkflowRunViewModel> StartFilesAsync(DownloadWorkset item, string? accountId,
        int? definitionId, CancellationToken ct, bool downloadsOnly = false)
    {
        if (downloadsOnly && definitionId != null)
            throw new ArgumentException("The download center does not accept a post-processing workflow.");
        if (string.IsNullOrWhiteSpace(item.OutputDirectory) || !Path.IsPathFullyQualified(item.OutputDirectory))
            throw new ArgumentException("Choose an absolute output directory.");
        storage.EnsurePathAllowed(item.OutputDirectory);
        if (item.Targets.Count == 0 || item.Targets.Any(t => t.Sources.Count != 1))
            throw new ArgumentException("Choose one mirror for each content target.");
        if (services.GetService<DownloadMethodRegistry>() is { } registry)
        {
            var resolved = new System.Collections.Generic.List<CloudDownloadTarget>();
            foreach (var target in item.Targets)
            {
                var source = await registry.ResolveAsync(target.Sources[0], ct);
                var method = await registry.DescribeAsync(source, target.AccountId ?? accountId, ct);
                if (!method.Ready) throw new InvalidOperationException(string.Join(" ", method.Requirements.Select(r => r.Message)));
                source = await registry.BindAccountAsync(source, target.AccountId ?? accountId, ct);
                resolved.Add(target with {Sources = [source], AccountId = source.AccountId});
            }
            item = item with {Targets = resolved};
        }
        else if (item.Targets.Any(t => t.Sources[0].ProviderId == "baidu"))
            accountId = await services.GetRequiredService<BaiduPcsAccountStore>().ResolveAccountIdAsync(accountId, ct);
        item = item with {Targets = item.Targets.Select(t => t with
        {
            SelectedSourceId = t.Sources[0].Id, AccountId = t.Sources[0].AccountId ?? t.AccountId ?? (t.Sources[0].ProviderId == "baidu" ? accountId : null),
            RemoteEntry = t.Sources[0].Kind == "remote" && (t.Sources[0].AccountId ?? t.AccountId ?? accountId) is { } remoteAccount
                ? new(t.Sources[0].ProviderId, remoteAccount, t.Sources[0].Url) : null
        }).ToList()};
        if (definitionId == null)
        {
            await SeedGate.WaitAsync(ct);
            try
            {
                var builtin = await db.Set<WorkflowDefinitionDbModel>().SingleOrDefaultAsync(d =>
                    d.Name == CloudWorkflow.BuiltinName && d.IsBuiltin && d.TriggerKind == CloudWorkflow.Trigger, ct);
                if (builtin == null)
                {
                    var created = await workflows.CreateAsync(new WorkflowDefinitionCreationInputModel
                    {
                        Name = CloudWorkflow.BuiltinName, TriggerKind = CloudWorkflow.Trigger, Enabled = true,
                        Activities = CloudWorkflow.Nodes.Select(k => new WorkflowActivityInputModel {Kind = k, ConfigJson = "{}"}).ToList()
                    }, ct);
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
        if (downloadsOnly && (!definition.Activities.Select(a => a.Kind).SequenceEqual(CloudWorkflow.Nodes) ||
                              definition.Activities.Any(a => !HasDefaultConfiguration(a.ConfigJson))))
            throw new InvalidOperationException("The built-in download workflow has changed. Use a workflow entry point for subsequent operations.");
        return WorkflowRunViewModel.From(await workflows.RunManagedAsync(definition.Id, item, ct));
    }

    // A legacy recipe must not override the directory, account or sources checked by the download center.
    private static bool HasDefaultConfiguration(string? config)
    {
        if (string.IsNullOrWhiteSpace(config)) return true;
        try
        {
            using var json = JsonDocument.Parse(config);
            return json.RootElement.ValueKind == JsonValueKind.Object && !json.RootElement.EnumerateObject().Any();
        }
        catch (JsonException) { return false; }
    }
}
